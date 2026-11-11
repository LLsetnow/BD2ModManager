using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using Spine;
using Spine.Unity;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace BD2PreviewInteractions;

[DefaultExecutionOrder(10000)]
internal sealed class PreviewInteractionController : MonoBehaviour,
    IPointerDownHandler, IPointerUpHandler, IDragHandler, IEndDragHandler, IScrollHandler
{
    private JObject scene = null!;
    private SkeletonGraphic[] graphics = System.Array.Empty<SkeletonGraphic>();
    private SkeletonGraphic graphic = null!;
    private RectTransform overlay = null!;
    private LocalAudioPlayer audio = null!;
    private LocalInteractionHud? hud;
    private Action<Exception>? onFailure;
    private readonly PointerGestureState pointerState = new();
    private readonly Dictionary<string, int> clicks = new();
    private readonly Dictionary<string, float> lastClick = new();
    private readonly Dictionary<string, float> hidden = new();
    private readonly Dictionary<string, Vector2> retainedBones = new();
    private JObject? group;
    private JObject? dragPoint;
    private JObject? holdPoint;
    private string previousPoint = "";
    private string pendingGroup = "";
    private TrackEntry? reaction;
    private TrackEntry? transition;
    private int tool;
    private int dragDestination = -1;
    private int holdDestination;
    private float gauge;
    private float elapsed;
    private float holdStart;
    private bool holdLoopStarted;
    private bool dragging;
    private bool started;
    private Vector2 pressScreen;
    private Vector2 dragBone;
    private Transform view = null!;
    private Vector3 initialViewPosition;
    private Vector3 initialViewScale;
    private Vector2 panStart;
    private Vector3 panViewStart;
    private bool panning;
    private float zoom = 1;
    private bool showPoints;

    internal void Initialize(JObject config, SkeletonGraphic[] skeletons, RectTransform rect, Transform viewTransform,
        LocalAudioPlayer audioPlayer, Action<Exception> failure)
    {
        scene = config;
        graphics = skeletons;
        overlay = rect;
        view = viewTransform;
        initialViewPosition = view.localPosition;
        initialViewScale = view.localScale;
        audio = audioPlayer;
        onFailure = failure;
        // A transparent raycast target keeps native controls out of this local scene.
        var image = gameObject.AddComponent<Image>();
        image.color = new Color(0, 0, 0, 0);
        image.raycastTarget = true;
    }

    internal void Begin()
    {
        ValidateScene();
        var first = (scene["groups"] as JArray)?.OfType<JObject>().FirstOrDefault();
        if (first == null) throw new InvalidOperationException("场景没有互动姿势");
        started = true;
        StartGroup(first, true);
    }

    internal void ValidateScene()
    {
        foreach (var entry in Groups)
        {
            var name = Str(entry, "skeleton");
            var target = string.IsNullOrEmpty(name) ? graphics.FirstOrDefault() :
                graphics.FirstOrDefault(g => g.SkeletonDataAsset.name.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0);
            if (target == null) throw new InvalidOperationException($"缺少姿势骨骼：{name}");
            var data = target.Skeleton.Data;
            if (data.FindAnimation(Str(entry, "animation")) == null)
                throw new InvalidOperationException($"原版骨骼缺少姿势动画：{Str(entry, "animation")}");
            foreach (var item in Items(entry, "tools"))
            {
                var skin = Str(item, "skin");
                if (skin.Length > 0 && data.FindSkin(skin) == null)
                    throw new InvalidOperationException($"原版骨骼缺少道具皮肤：{skin}");
            }
            var missing = Points.Where(p => Int(p, "group") == Int(entry, "id") && target.Skeleton.FindBone(Str(p, "bone")) == null).ToArray();
            if (missing.Length > 0) throw new InvalidOperationException($"原版骨骼缺少触点：{Str(missing[0], "bone")}");
        }
    }

    internal void Shutdown()
    {
        started = false;
        StopAudio();
        pointerState.Cancel();
        CancelDrag();
        holdPoint = null;
    }

    private static string Str(JToken? node, string key) => node?[key]?.Value<string>() ?? "";
    private static int Int(JToken? node, string key, int fallback = 0) => node?[key]?.Value<int?>() ?? fallback;
    private static float Float(JToken? node, string key, float fallback = 0) => node?[key]?.Value<float?>() ?? fallback;
    private static bool Bool(JToken? node, string key) => node?[key]?.Value<bool?>() ?? false;
    private static JArray Items(JToken? node, string key) => node?[key] as JArray ?? new JArray();
    private static float X(JToken? node) => Float(node, "x");
    private static float Y(JToken? node) => Float(node, "y");
    private static Vector2 Pair(JToken? node) => node is JArray p && p.Count >= 2
        ? new Vector2(p[0]?.Value<float>() ?? 0, p[1]?.Value<float>() ?? 0) : Vector2.zero;
    private float UnitScale => graphic.SkeletonDataAsset.scale;
    private Vector2[] Polygon(JToken? node) => (node as JArray)?.Select(p => Pair(p) * UnitScale).ToArray() ?? System.Array.Empty<Vector2>();
    private static float Clip(float value, float lo, float hi) => Mathf.Clamp(value, lo, hi);
    private IEnumerable<JObject> Groups => (scene["groups"] as JArray)?.OfType<JObject>() ?? Enumerable.Empty<JObject>();
    private IEnumerable<JObject> Points => (scene["points"] as JArray)?.OfType<JObject>() ?? Enumerable.Empty<JObject>();
    private Skeleton Skeleton => graphic.Skeleton;
    private Spine.AnimationState State => graphic.AnimationState;

    private void StartGroup(JObject next, bool initial = false, string motion = "")
    {
        pointerState.Cancel();
        panning = false;
        CancelHold();
        CancelDrag();
        retainedBones.Clear();
        clicks.Clear();
        lastClick.Clear();
        previousPoint = "";
        hidden.Clear();
        if (!initial && !string.IsNullOrEmpty(motion) && graphic != null && Skeleton.Data.FindAnimation(motion) != null)
        {
            reaction = null;
            transition = SpinePoseTransitions.StartMotion(State, Skeleton, motion);
            pendingGroup = Int(next, "id").ToString();
            return;
        }
        pendingGroup = "";
        var requested = Str(next, "skeleton");
        var match = string.IsNullOrEmpty(requested)
            ? (graphic != null ? graphic : graphics.FirstOrDefault())
            : graphics.FirstOrDefault(g => g != null && g.SkeletonDataAsset.name.IndexOf(requested, StringComparison.OrdinalIgnoreCase) >= 0);
        if (match == null) throw new InvalidOperationException($"缺少原版骨骼：{requested}");
        for (var parent = match.transform; parent != null && parent != overlay.parent; parent = parent.parent)
            parent.gameObject.SetActive(true);
        foreach (var g in graphics) if (g != null) g.gameObject.SetActive(g == match);
        graphic = match;
        graphic.Initialize(false);
        State.ClearTracks();
        reaction = null;
        Skeleton.SetToSetupPose();
        group = next;
        gauge = Float(next["gauge"], "StartScore");
        if (initial || !Bool(next, "keepTool") || !Items(next, "tools").Any(t => Int(t, "id") == tool))
            tool = Items(next, "tools").FirstOrDefault() is JToken first ? Int(first, "id") : 0;
        ApplyTool();
        var idle = Str(next, "animation");
        if (Skeleton.Data.FindAnimation(idle) == null) throw new InvalidOperationException($"缺少原版动画：{idle}");
        var switchAnimation = initial ? "" : Str(next, "switchAnimation");
        if (switchAnimation.Length > 0 && Skeleton.Data.FindAnimation(switchAnimation) != null)
        {
            transition = State.SetAnimation(0, switchAnimation, false);
            transition.MixDuration = 0;
            var queued = State.AddAnimation(0, idle, true, 0);
            queued.MixDuration = 0;
        }
        else { State.SetAnimation(0, idle, true); transition = null; }
        // SkeletonGraphic may already have updated this frame. Apply the new
        // pose now so its setup/default attachments cannot flash until next frame.
        SpinePoseTransitions.ApplyCurrent(State, Skeleton);
    }

    private void ApplyTool()
    {
        var skin = Items(group, "tools").FirstOrDefault(t => Int(t, "id") == tool)?["skin"]?.Value<string>();
        if (!string.IsNullOrEmpty(skin) && Skeleton.Data.FindSkin(skin) != null)
        {
            Skeleton.SetSkin(skin);
            Skeleton.SetSlotsToSetupPose();
        }
    }

    private void LateUpdate() => Guard(() => hud?.Refresh());

    private void Update()
    {
        Guard(Tick);
    }

    private void Tick()
    {
        if (!started || group == null) return;
        elapsed += Time.unscaledDeltaTime;
        if (transition == null && pendingGroup.Length == 0)
        {
            var animation = State.GetCurrent(0)?.Animation.Name;
            var activeGroup = Groups.FirstOrDefault(g => Str(g, "animation") == animation &&
                (Str(g, "skeleton").Length == 0 || graphic.SkeletonDataAsset.name.IndexOf(Str(g, "skeleton"), StringComparison.OrdinalIgnoreCase) >= 0));
            if (activeGroup != null && activeGroup != group) StartGroup(activeGroup);
        }
        if (transition != null && (transition.IsComplete ||
            (pendingGroup.Length == 0 && State.GetCurrent(0) != transition)))
        {
            transition = null;
            if (pendingGroup.Length > 0)
            {
                var id = int.Parse(pendingGroup);
                pendingGroup = "";
                var next = Groups.FirstOrDefault(g => Int(g, "id") == id);
                if (next != null) StartGroup(next);
            }
        }
        foreach (var key in hidden.Where(h => h.Value <= elapsed).Select(h => h.Key).ToArray())
        {
            hidden.Remove(key);
            clicks.Remove(key);
            var parent = Points.FirstOrDefault(p => Items(p["hidden"], "points").Any(x => x?.Value<string>() == key));
            if (Bool(parent?["hidden"], "reset"))
            {
                retainedBones.Clear();
                Skeleton.SetToSetupPose();
                State.Apply(Skeleton);
                Skeleton.UpdateWorldTransform();
                dragPoint = null;
                clicks.Clear();
            }
            if (Str(holdPoint, "key") == key) CancelHold();
            if (Str(dragPoint, "key") == key) CancelDrag();
        }
        var cfg = group["gauge"];
        var limits = cfg?["AutoDecreaseScoreMinMaxLimit"];
        if (Bool(cfg, "IsOn") && gauge >= X(limits) && gauge <= Y(limits))
            AddScore(Clip(gauge + Float(cfg, "AutoDecreaseScorePerSecond") * Time.unscaledDeltaTime, X(limits), Y(limits)) - gauge);
        if (holdPoint != null)
        {
            if (!holdLoopStarted && (reaction == null || reaction.IsComplete))
            {
                var name = Items(holdPoint["hold"], "loop").ElementAtOrDefault(holdDestination)?.Value<string>();
                PlayAnimation(2, name, true);
                PlayAudio(holdPoint, "loop", holdDestination);
                holdLoopStarted = true;
            }
            if (Int(holdPoint["hold"], "mode") == 1 && elapsed - holdStart >= Float(holdPoint["hold"], "time")) FinishHold(true);
        }
        foreach (var pair in retainedBones)
        {
            var bone = Skeleton.FindBone(pair.Key);
            if (bone == null) continue;
            bone.X = pair.Value.x;
            bone.Y = pair.Value.y;
        }
        if (dragPoint != null && dragging)
        {
            var bone = Skeleton.FindBone(Str(dragPoint, "bone"));
            if (bone != null)
            {
                bone.X = dragBone.x;
                bone.Y = dragBone.y;
                Skeleton.UpdateWorldTransform();
                var collider = Polygon(dragPoint["collider"]).Select(p => BoneToWorld(bone, p)).ToArray();
                var destination = -1;
                var regions = Items(dragPoint, "destinations");
                for (var i = 0; i < regions.Count; i++)
                {
                    var region = regions[i];
                    var targetBone = Skeleton.FindBone(Str(region, "bone"));
                    if (targetBone != null && Overlap(collider, Polygon(region?["corners"]).Select(p => BoneToWorld(targetBone, p)).ToArray()))
                    { destination = i; break; }
                }
                if (destination != dragDestination)
                {
                    dragDestination = destination;
                    if (destination >= 0) PlayAudio(dragPoint, "motion", destination);
                }
                if (destination < 0)
                {
                    if (holdPoint == dragPoint) CancelHold();
                }
                else if (Int(dragPoint["hold"], "mode") > 0)
                {
                    if (holdPoint != null && holdDestination != destination) CancelHold();
                    if (holdPoint == null) StartHold(dragPoint, destination);
                }
                else ChangeGroup(dragPoint, destination, false);
            }
        }
        else if (retainedBones.Count > 0) Skeleton.UpdateWorldTransform();
    }

    private static bool Contains(Vector2[] polygon, Vector2 point)
    {
        if (polygon.Length < 3) return false;
        var positive = false; var negative = false;
        for (var i = 0; i < polygon.Length; i++)
        {
            var a = polygon[i]; var b = polygon[(i + 1) % polygon.Length];
            var cross = (b.x - a.x) * (point.y - a.y) - (b.y - a.y) * (point.x - a.x);
            positive |= cross > 0; negative |= cross < 0;
        }
        return !(positive && negative);
    }

    private static bool Overlap(Vector2[] a, Vector2[] b)
    {
        if (a.Length < 3 || b.Length < 3) return false;
        foreach (var polygon in new[] { a, b })
            for (var i = 0; i < polygon.Length; i++)
            {
                var edge = polygon[(i + 1) % polygon.Length] - polygon[i];
                var axis = new Vector2(-edge.y, edge.x);
                var av = a.Select(p => Vector2.Dot(p, axis)).ToArray();
                var bv = b.Select(p => Vector2.Dot(p, axis)).ToArray();
                if (av.Max() < bv.Min() || bv.Max() < av.Min()) return false;
            }
        return true;
    }

    private static Vector2 BoneToWorld(Bone bone, Vector2 local)
    {
        bone.LocalToWorld(local.x, local.y, out var x, out var y);
        return new Vector2(x, y);
    }
    private static Vector2 WorldToBone(Bone bone, Vector2 world)
    {
        bone.WorldToLocal(world.x, world.y, out var x, out var y);
        return new Vector2(x, y);
    }
    private Vector2 ScreenToSkeleton(Vector2 screen)
    {
        var rect = graphic.transform as RectTransform;
        var canvas = graphic.canvas;
        var camera = canvas == null || canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(rect, screen, camera, out var local);
        return local / MeshScale();
    }

    private float MeshScale()
    {
        var scale = graphic.canvas != null ? graphic.canvas.referencePixelsPerUnit : 100f;
        var mode = HarmonyLib.AccessTools.Field(typeof(SkeletonGraphic), "layoutScaleMode")?.GetValue(graphic);
        if (mode != null && Convert.ToInt32(mode) != 0)
        {
            var reference = HarmonyLib.AccessTools.Field(typeof(SkeletonGraphic), "referenceScale")?.GetValue(graphic) is float value ? value : 1f;
            var layout = HarmonyLib.AccessTools.Method(typeof(SkeletonGraphic), "GetLayoutScale")?.Invoke(graphic, new[] { mode }) is float ratio ? ratio : 1f;
            scale *= reference * layout;
        }
        return Mathf.Abs(scale) < 0.0001f ? 1 : scale;
    }

    private Vector2 ViewportPosition(Vector2 screen)
    {
        var parent = overlay.parent as RectTransform;
        var canvas = graphic.canvas;
        var camera = canvas == null || canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, screen, camera, out var local);
        return local;
    }

    private void BeginPan(PointerEventData data)
    {
        panning = true;
        panStart = ViewportPosition(data.position);
        panViewStart = view.localPosition;
        data.Use();
    }

    private bool OverLocalControls(Vector2 screen) => hud?.ContainsControl(screen) ?? false;

    public void OnScroll(PointerEventData data) => Guard(() => HandleScroll(data));
    private void HandleScroll(PointerEventData data)
    {
        if (!started || graphic == null || OverLocalControls(data.position)) return;
        var next = Mathf.Clamp(zoom * Mathf.Exp(data.scrollDelta.y * 0.12f), 0.25f, 4f);
        var ratio = next / zoom;
        var pivot = ViewportPosition(data.position);
        var position = (Vector2)view.localPosition;
        var moved = pivot + (position - pivot) * ratio;
        view.localPosition = new Vector3(moved.x, moved.y, view.localPosition.z);
        zoom = next;
        view.localScale = initialViewScale * zoom;
        data.Use();
    }

    private IEnumerable<JObject> ActivePoints()
    {
        var posePoints = Points.Where(p => Int(p, "group") == Int(group, "id") && Int(p, "tool") == tool).ToArray();
        var exclusive = posePoints.FirstOrDefault(p => Bool(p["hidden"], "exclusive") &&
            Items(p["hidden"], "points").Any(k => hidden.ContainsKey(k?.Value<string>() ?? "")));
        return posePoints.Where(p =>
            Int(p, "group") == Int(group, "id") && Int(p, "tool") == tool &&
            (exclusive == null || Items(exclusive["hidden"], "points").Any(k => k?.Value<string>() == Str(p, "key"))) &&
            (!posePoints.Any(parent => Items(parent["hidden"], "points").Any(k => k?.Value<string>() == Str(p, "key"))) || hidden.ContainsKey(Str(p, "key"))) &&
            (!Bool(p["gauge"], "IsOn") || (Mathf.RoundToInt(gauge) >= X(p["gauge"]?["InteractableMinMaxRange"]) &&
                Mathf.RoundToInt(gauge) <= Y(p["gauge"]?["InteractableMinMaxRange"]))) &&
            Skeleton.FindBone(Str(p, "bone"))?.Active == true);
    }

    public void OnPointerDown(PointerEventData data) => Guard(() => HandlePointerDown(data));
    private void HandlePointerDown(PointerEventData data)
    {
        if (!started || group == null || OverLocalControls(data.position)) return;
        if (!pointerState.Begin((int)data.button, data.pointerId, Time.unscaledTime)) return;
        data.Use();
        if (data.button == PointerEventData.InputButton.Right) { BeginPan(data); return; }
        if (transition != null || dragPoint != null || holdPoint != null) return;
        var world = ScreenToSkeleton(data.position);
        var markerKey = VisibleMarkers().Where(m => Vector2.Distance(m.Screen, data.position) <= (m.Drag ? 22 : 14))
            .Select(m => m.Key).FirstOrDefault();
        foreach (var point in ActivePoints())
        {
            var bone = Skeleton.FindBone(Str(point, "bone"));
            if (bone == null || (markerKey != null ? markerKey != Str(point, "key") :
                !Contains(Polygon(point["corners"]), WorldToBone(bone, world)))) continue;
            data.Use();
            var key = Str(point, "key");
            if (previousPoint != key) clicks.Remove(previousPoint);
            previousPoint = key;
            pressScreen = data.position;
            if (Bool(point, "drag"))
            {
                dragPoint = point;
                dragBone = new Vector2(bone.X, bone.Y);
                dragging = false;
                dragDestination = -1;
                return;
            }
            if (!pointerState.ConsumeTap()) return;
            if (Int(point, "clickMax") > 1 && elapsed - (lastClick.TryGetValue(key, out var time) ? time : 0) > Float(point, "clickReset")) clicks.Remove(key);
            var count = clicks.TryGetValue(key, out var current) ? current : 0;
            lastClick[key] = elapsed;
            var animations = Items(point, "animations");
            var index = Bool(point, "random") ? UnityEngine.Random.Range(0, animations.Count) : Math.Min(count, animations.Count - 1);
            clicks[key] = count + 1;
            var changePose = Int(point, "nextGroup") != 0 && Int(point["hold"], "mode") == 0 && count + 1 >= Int(point, "clickMax", 1);
            if (!changePose) { PlayReaction(animations.ElementAtOrDefault(index)?.Value<string>()); PlayAudio(point, "click", index); }
            var scores = Items(point["gauge"], "Scores");
            if (Bool(point["gauge"], "IsOn") && scores.Count > 0) AddScore(scores[Math.Min(count, scores.Count - 1)]?.Value<float>() ?? 0);
            Reveal(point);
            // Apply the old pose's point score before switching/resetting the next pose.
            if (changePose) ChangeGroup(point);
            else if (Int(point["hold"], "mode") != 0) StartHold(point, 0);
            return;
        }
        // Empty-space left presses intentionally do nothing.
    }

    public void OnDrag(PointerEventData data) => Guard(() => HandleDrag(data));
    private void HandleDrag(PointerEventData data)
    {
        if (!pointerState.Owns((int)data.button, data.pointerId)) return;
        if (panning && data.button == PointerEventData.InputButton.Right)
        {
            var delta = ViewportPosition(data.position) - panStart;
            view.localPosition = panViewStart + new Vector3(delta.x, delta.y, 0);
            data.Use();
            return;
        }
        if (dragPoint == null) return;
        data.Use();
        if (!dragging)
        {
            if (!pointerState.TryStartDrag(data.pointerId, Time.unscaledTime, Plugin.Settings.DragHoldSeconds,
                (data.position - pressScreen).sqrMagnitude)) return;
            dragging = true;
            PlayReaction(Items(dragPoint, "animations").FirstOrDefault()?.Value<string>());
            PlayAudio(dragPoint, "click", 0);
        }
        var bone = Skeleton.FindBone(Str(dragPoint, "bone"));
        if (bone?.Parent == null) return;
        var world = ScreenToSkeleton(data.position);
        var zone = dragPoint["zone"];
        if (zone is JObject)
        {
            var parent = Skeleton.FindBone(Str(zone, "bone"));
            var corners = Polygon(zone["corners"]);
            if (parent != null && corners.Length >= 4)
            {
                var local = WorldToBone(parent, world);
                var a = corners[0]; var u = corners[3] - a; var v = corners[1] - a;
                var det = u.x * v.y - u.y * v.x;
                if (Mathf.Abs(det) > 0.0001f)
                {
                    var diff = local - a;
                    var pu = Clip((diff.x * v.y - diff.y * v.x) / det, 0, 1);
                    var pv = Clip((diff.y * u.x - diff.x * u.y) / det, 0, 1);
                    world = BoneToWorld(parent, a + pu * u + pv * v);
                }
            }
        }
        else
        {
            var center = BoneToWorld(bone.Parent, new Vector2(bone.Data.X, bone.Data.Y));
            var delta = world - center;
            var radius = Float(dragPoint, "radius") * UnitScale;
            if (radius > 0 && delta.magnitude > radius) world = center + delta.normalized * radius;
        }
        dragBone = WorldToBone(bone.Parent, world);
    }

    public void OnPointerUp(PointerEventData data) => Guard(() => HandlePointerUp(data));
    private void HandlePointerUp(PointerEventData data)
    {
        if (!pointerState.Owns((int)data.button, data.pointerId)) return;
        if (data.button == PointerEventData.InputButton.Right)
        { panning = false; pointerState.End((int)data.button, data.pointerId); data.Use(); return; }
        if (dragPoint != null && dragging && dragPoint["resetOnRelease"]?.Value<bool?>() == false)
            retainedBones[Str(dragPoint, "bone")] = dragBone;
        if (holdPoint != null)
        {
            FinishHold(elapsed - holdStart >= Float(holdPoint["hold"], "time"));
            pointerState.End((int)data.button, data.pointerId);
            data.Use();
            return;
        }
        var point = dragPoint;
        var wasDragging = dragging;
        CancelDrag();
        pointerState.End((int)data.button, data.pointerId);
        if (point == null) return;
        if (wasDragging)
        {
            PlayReaction(Items(point, "animations").ElementAtOrDefault(1)?.Value<string>());
            PlayAudio(point, "click", 1);
        }
        data.Use();
    }

    private void CancelDrag()
    {
        var wasDragging = dragPoint != null && dragging;
        dragPoint = null;
        dragging = false;
        if (wasDragging && graphic != null)
        {
            Skeleton.SetToSetupPose();
            State.Apply(Skeleton);
            Skeleton.UpdateWorldTransform();
        }
    }

    public void OnEndDrag(PointerEventData data) => OnPointerUp(data);

    private void StartHold(JObject point, int destination)
    {
        holdPoint = point; holdDestination = destination; holdStart = elapsed; holdLoopStarted = false;
        var parent = Points.FirstOrDefault(p => Items(p["hidden"], "points").Any(k => k?.Value<string>() == Str(point, "key")));
        if (parent != null) foreach (var key in Items(parent["hidden"], "points")) hidden[key?.Value<string>() ?? ""] = float.PositiveInfinity;
    }
    private void CancelHold()
    {
        if (holdPoint == null) return;
        StopAudio();
        State.ClearTrack(2);
        var parent = Points.FirstOrDefault(p => Items(p["hidden"], "points").Any(k => k?.Value<string>() == Str(holdPoint, "key")));
        if (parent != null) foreach (var key in Items(parent["hidden"], "points"))
        {
            var name = key?.Value<string>() ?? "";
            if (hidden.TryGetValue(name, out var until) && float.IsPositiveInfinity(until)) hidden[name] = elapsed + Float(parent["hidden"], "duration");
        }
        holdPoint = null; holdLoopStarted = false;
    }
    private void FinishHold(bool success)
    {
        var point = holdPoint;
        if (point == null || !pointerState.CompleteHoldOnce()) return;
        var destination = holdDestination;
        CancelHold();
        CancelDrag();
        if (success && Int(point, "nextGroup") != 0) { ChangeGroup(point, destination, !Bool(point, "drag")); return; }
        var kind = success ? "success" : "fail";
        var animation = Items(point["hold"], kind).ElementAtOrDefault(destination)?.Value<string>();
        if (!string.IsNullOrEmpty(animation)) { PlayReaction(animation); PlayAudio(point, kind, destination); }
        if (Bool(point["gauge"], "IsOn")) AddScore(Float(point["gauge"], success ? "LongPressSuccessScore" : "LongPressFailScore"));
        Reveal(point);
    }
    private void Reveal(JObject point)
    {
        var duration = reaction?.Animation.Duration ?? 0;
        foreach (var key in Items(point["hidden"], "points")) hidden[key?.Value<string>() ?? ""] = elapsed + duration + Float(point["hidden"], "duration");
        var parent = Points.FirstOrDefault(p => Items(p["hidden"], "points").Any(k => k?.Value<string>() == Str(point, "key")));
        if (parent != null) foreach (var key in Items(parent["hidden"], "points"))
            hidden[key?.Value<string>() ?? ""] = elapsed + duration + (Bool(point["hidden"], "closeParent") ? 0 : Float(parent["hidden"], "duration"));
    }
    private void ChangeGroup(JObject point, int destination = 0, bool playSound = true)
    {
        var next = Groups.FirstOrDefault(g => Int(g, "id") == Int(point, "nextGroup"));
        if (next == null) return;
        if (playSound) PlayAudio(point, "motion", destination);
        StartGroup(next, false, Items(point, "motions").ElementAtOrDefault(destination)?.Value<string>() ?? "");
    }
    private void AddScore(float amount)
    {
        var config = group?["gauge"];
        if (!Bool(config, "IsOn")) return;
        var previous = Mathf.RoundToInt(gauge);
        gauge = Clip(gauge + amount, X(config?["MinMaxScore"]), Y(config?["MinMaxScore"]));
        var current = Mathf.RoundToInt(gauge);
        var motions = Items(config, "MotionNameByScoreList").OfType<JObject>();
        if (current > previous) motions = motions.Reverse();
        var motion = motions.FirstOrDefault(m => current > previous
            ? Bool(m, "IsCanPlayWhenIncreaseScore") && previous < Int(m, "Score") && Int(m, "Score") <= current
            : Bool(m, "IsCanPlayWhenDecreaseScore") && current <= Int(m, "Score") && Int(m, "Score") < previous);
        if (motion == null || Str(motion, "MixAnimationName").Length == 0) return;
        PlayReaction(Str(motion, "MixAnimationName"));
        PlayEvents(Str(motion, "VoiceSoundEventName") is string voice && voice.Length > 0 ? $"event:/Voices/{voice}_KR" : "", Str(motion, "SFXSoundEventName"));
    }
    private void PlayReaction(string? name)
    {
        if (string.IsNullOrEmpty(name) || Skeleton.Data.FindAnimation(name) == null) return;
        State.ClearTrack(1);
        Skeleton.SetToSetupPose();
        reaction = State.SetAnimation(1, name, false);
        reaction.MixDuration = 0;
        State.Apply(Skeleton);
        Skeleton.UpdateWorldTransform();
    }
    private void PlayAnimation(int track, string? name, bool loop)
    {
        if (!string.IsNullOrEmpty(name) && Skeleton.Data.FindAnimation(name) != null) State.SetAnimation(track, name, loop);
    }

    private void PlayAudio(JObject point, string action, int index)
    {
        if (index < 0) return;
        var row = scene["audio"]?["points"]?[$"{Int(point, "group")}:{Int(point, "id")}"];
        if (row == null) return;
        var prefix = action == "click" ? "sound" : action == "motion" ? "soundMotion" : "longPress" + char.ToUpperInvariant(action[0]) + action.Substring(1);
        string Name(string kind)
        {
            var names = Items(row, prefix + kind + "Name");
            var auto = action != "click" && action != "motion" && Int(row, "isLongPress" + kind + "AutoIndex") != 0;
            var name = names.ElementAtOrDefault(auto ? 0 : index)?.Value<string>() ?? "";
            return auto && name.Length > 0 ? $"{name}_{index + 1:00}" : name;
        }
        var voice = Name("Voice");
        var fx = Name("FX");
        var voiceLoop = action == "loop" && Items(row, "isLoopLongPressLoopVoice").ElementAtOrDefault(Int(row, "isLongPressVoiceAutoIndex") != 0 ? 0 : index)?.Value<int?>() == 1;
        var fxLoop = action == "loop" && Items(row, "isLoopLongPressLoopFx").ElementAtOrDefault(Int(row, "isLongPressFXAutoIndex") != 0 ? 0 : index)?.Value<int?>() == 1;
        PlayEvents(voice.Length > 0 ? $"event:/Voices/{voice}_KR" : "", fx, voiceLoop, fxLoop);
    }
    private void PlayEvents(string voice, string fx, bool voiceLoop = false, bool fxLoop = false)
    {
        var events = scene["audio"]?["events"];
        var rows = new[] { (name: voice, loop: voiceLoop), (name: fx, loop: fxLoop) }
            .Select(e => (hash: Str(events?[e.name], "file"), e.loop)).Where(e => e.hash.Length > 0).ToArray();
        if (rows.Length == 0) return;
        audio.Play(rows.Select(row => (row.hash, row.loop)).ToArray());
    }
    private void StopAudio()
    {
        if (audio != null) audio.Stop();
    }

    private void OnGUI()
    {
        if (!started || group == null) return;
        var resetBounds = hud?.ResetClickRect ?? default;
        // Keep click ownership separate from the native visual Button. A native
        // CanvasGroup or animation must not swallow the local reset action.
        if (resetBounds.width > 0 && GUI.Button(resetBounds, GUIContent.none, GUIStyle.none)) RequestReset();
        var bounds = hud?.PointToggleRect ?? default;
        if (bounds.width > 0 && GUI.Button(bounds, showPoints ? "隐藏触点" : "显示触点")) showPoints = !showPoints;
    }

    internal bool PointsVisible => showPoints;
    internal JObject? CurrentGroup => group;
    internal int CurrentTool => tool;
    internal float CurrentGauge => gauge;
    internal int PoseIndex => group == null ? 0 : Groups.ToList().IndexOf(group) + 1;
    internal bool IsTransitioning => transition != null;
    internal void AttachHud(LocalInteractionHud value) => hud = value;

    internal IEnumerable<(string Key, Vector2 Screen, bool Drag)> VisibleMarkers()
    {
        if (!started || !showPoints || group == null || transition != null) yield break;
        var canvas = graphic.canvas;
        var camera = canvas == null || canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
        var focused = dragPoint ?? holdPoint;
        foreach (var point in focused != null ? new[] { focused } : ActivePoints())
        {
            var bone = Skeleton.FindBone(Str(point, "bone"));
            var corners = Polygon(point["corners"]);
            if (bone == null || corners.Length == 0 || Int(point, "group") != Int(group, "id") || Int(point, "tool") != tool) continue;
            var center = corners.Aggregate(Vector2.zero, (sum, value) => sum + value) / corners.Length;
            var world = BoneToWorld(bone, center) * MeshScale();
            var screen = RectTransformUtility.WorldToScreenPoint(camera, graphic.transform.TransformPoint(world));
            yield return (Str(point, "key"), screen, Bool(point, "drag"));
        }
    }

    internal void RequestReset() => Guard(() =>
    {
        Restart();
        Plugin.Instance?.LogDiagnostic($"Local Reset applied: skeleton={Str(scene, "skeleton")}, pose={PoseIndex}, gauge={gauge}");
    });

    internal void Restart()
    {
        if (!started) return;
        pointerState.Cancel(); panning = false;
        CancelHold(); CancelDrag(); StopAudio();
        retainedBones.Clear(); clicks.Clear(); hidden.Clear(); lastClick.Clear();
        previousPoint = ""; pendingGroup = ""; transition = null; reaction = null; elapsed = 0;
        var first = Groups.FirstOrDefault();
        if (first != null) StartGroup(first, true);
    }

    private void OnApplicationFocus(bool focused)
    {
        if (focused || !started) return;
        pointerState.Cancel(); panning = false;
        CancelHold(); CancelDrag(); StopAudio();
    }

    private void Guard(Action action)
    {
        if (!started) return;
        try { action(); }
        catch (Exception error)
        {
            started = false;
            pointerState.Cancel();
            StopAudio();
            onFailure?.Invoke(error);
        }
    }

    internal void SelectTool(int id)
    {
        if (group == null || transition != null || !Items(group, "tools").Any(t => Int(t, "id") == id)) return;
        pointerState.Cancel();
        CancelHold(); CancelDrag(); StopAudio();
        retainedBones.Clear(); clicks.Clear(); hidden.Clear(); lastClick.Clear(); previousPoint = "";
        State.ClearTrack(1); State.ClearTrack(2); reaction = null;
        Skeleton.SetToSetupPose();
        State.Apply(Skeleton);
        tool = id;
        ApplyTool();
        State.Apply(Skeleton);
        Skeleton.UpdateWorldTransform();
    }
}
