using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Spine;
using BD2PreviewInteractions;

var path = args[0];
using var atlasText = File.OpenText(Path.ChangeExtension(path, ".atlas"));
var atlas = new Atlas(atlasText, Path.GetDirectoryName(path), new NoTextures());
var reader = new SkeletonJson(new AtlasAttachmentLoader(atlas)) { Scale = .01f };
using var json = File.OpenText(path);
var data = reader.ReadSkeletonData(json);
Console.WriteLine($"Loaded actual Granhildr mod: {data.Slots.Count} slots, {data.Animations.Count} animations");
var motion = data.FindAnimation("motion1_18") ?? throw new Exception("Missing transition animation");
var reactions = data.Animations.Where(a => a.Name.StartsWith("mix1_", StringComparison.Ordinal) && a.Timelines.Count > 0).ToArray();
int samples = 0, legacyDifferences = 0, legacyLegFrames = 0;
var names = new HashSet<string>();
var reference = Frames(null, true);
if (reference.Any(frame => frame.Pose1Leg && frame.Pose2Leg)) throw new Exception("Clean transition displays both poses' legs");
foreach (var reaction in reactions)
{
    var old = Frames(reaction, false);
    var fixedFrames = Frames(reaction, true);
    for (var i = 0; i < reference.Count; i++)
    {
        samples++;
        if (old[i].Pose1Leg && !reference[i].Pose1Leg) legacyLegFrames++;
        if (old[i] != reference[i]) { legacyDifferences++; names.Add(reaction.Name); }
        if (fixedFrames[i] != reference[i]) throw new Exception($"Reaction leaked into transition: {reaction.Name}, frame {i}");
    }
}
var holdFrames = Frames(reactions[0], true, withHold: true);
if (!holdFrames.SequenceEqual(reference)) throw new Exception("Hold layer leaked into motion");
if (legacyDifferences == 0 || legacyLegFrames == 0) throw new Exception("Failed to reproduce old pose1 leg leakage");
Console.WriteLine($"Reproduced old leakage in {legacyDifferences} samples across {names.Count} reactions; stale pose1 leg in {legacyLegFrames} frames");
Console.WriteLine($"Passed {samples} real-mod slot/alpha snapshots across {reactions.Length} pose1 reactions");

List<(int Hash, bool Pose1Leg, bool Pose2Leg)> Frames(Spine.Animation reaction, bool clean, bool withHold = false)
{
    var skeleton = new Skeleton(data);
    skeleton.SetSkin("default"); skeleton.SetToSetupPose();
    var state = new Spine.AnimationState(new AnimationStateData(data) { DefaultMix = .15f });
    state.SetAnimation(0, "idle1", true);
    state.Update(.5f); state.Apply(skeleton);
    if (reaction != null)
    {
        var entry = state.SetAnimation(1, reaction, false); entry.MixDuration = 0;
        state.Update(reaction.Duration + .1f); state.Apply(skeleton);
        if (withHold)
        {
            var hold = state.SetAnimation(2, reaction, true); hold.MixDuration = 0;
            state.Apply(skeleton);
        }
    }
    if (clean)
    {
        SpinePoseTransitions.StartMotion(state, skeleton, motion.Name);
        if (state.GetCurrent(1) != null || state.GetCurrent(2) != null) throw new Exception("Overlay tracks not cleared");
    }
    else
    {
        var entry = state.SetAnimation(0, motion, false); entry.MixDuration = 0;
    }
    var frames = new List<(int Hash, bool Pose1Leg, bool Pose2Leg)>();
    for (var time = 0f; time <= motion.Duration + .1f; time += 1f / 60)
    {
        state.Update(1f / 60); state.Apply(skeleton); skeleton.UpdateWorldTransform();
        var hash = new HashCode();
        foreach (var slot in skeleton.Slots)
        {
            hash.Add(slot.Attachment?.Name, StringComparer.Ordinal);
            hash.Add((int)Math.Round(slot.A * 10000));
        }
        var leg = skeleton.FindSlot("ID1_leg_R");
        var nextLeg = skeleton.FindSlot("ID2_legs2");
        frames.Add((hash.ToHashCode(), leg?.Attachment != null && leg.A > .01f,
            nextLeg?.Attachment != null && nextLeg.A > .01f));
    }
    state.ClearTracks(); skeleton.SetToSetupPose();
    state.SetAnimation(0, "idle2", true);
    SpinePoseTransitions.ApplyCurrent(state, skeleton);
    if (skeleton.FindSlot("ID1_leg_R")?.Attachment != null) throw new Exception("Pose1 leg persists after entering pose2");
    if (skeleton.FindSlot("ID2_legs2")?.Attachment == null) throw new Exception("Pose2 leg missing");
    state.ClearTracks(); skeleton.SetToSetupPose();
    state.SetAnimation(0, "idle1", true);
    SpinePoseTransitions.ApplyCurrent(state, skeleton);
    if (skeleton.FindSlot("ID2_legs2")?.Attachment != null || skeleton.FindSlot("ID1_leg_R")?.Attachment == null)
        throw new Exception("Reset to pose1 leaves pose2 attachments");
    return frames;
}

sealed class NoTextures : TextureLoader
{
    public void Load(AtlasPage page, string path) { page.rendererObject = new object(); }
    public void Unload(object texture) { }
}
