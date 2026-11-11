using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using Spine;
using Spine.Unity;
using BD2PreviewInteractions;

int checks = 0;
void Check(bool result, string message) { checks++; if (!result) throw new Exception(message); }
SkeletonData CreateData()
{
    var data = new SkeletonData();
    data.Bones.Add(new BoneData(0, "root", null));
    data.Animations.Add(new Spine.Animation("idle1", new ExposedList<Timeline>(), 1));
    data.Animations.Add(new Spine.Animation("idle2", new ExposedList<Timeline>(), 1));
    return data;
}
// Only the managed asset initialization API is exercised. No Unity object
// creation, rendering, TextAsset, or other native engine operation is invoked.
var asset = (SkeletonDataAsset)RuntimeHelpers.GetUninitializedObject(typeof(SkeletonDataAsset));
asset.fromAnimation = new[] { "idle1" };
asset.toAnimation = new[] { "idle2" };
asset.duration = new[] { .35f };
asset.defaultMix = .15f;
var data = CreateData();
ManagedSkeletonLoader.SetData(asset, data);
var cache = typeof(SkeletonDataAsset).GetField("stateData", BindingFlags.Instance | BindingFlags.NonPublic);
Check(cache.GetValue(asset) != null, "Background-loaded skeleton must also initialize its animation state cache");
var stateData = asset.GetAnimationStateData();
Check(ReferenceEquals(stateData.SkeletonData, data), "Animation state and graphic must use the same skeleton");
Check(Math.Abs(stateData.DefaultMix - .15f) < .0001f, "Default mix must be preserved");
Check(Math.Abs(stateData.GetMix(data.FindAnimation("idle1"), data.FindAnimation("idle2")) - .35f) < .0001f, "Configured transition mix must be preserved");
var state = new Spine.AnimationState(stateData);
state.SetAnimation(0, "idle1", true);
state.Update(.1f);
state.Apply(new Skeleton(data));
Check(state.GetCurrent(0).Animation.Name == "idle1", "First pose can be started without null data");
var replacement = CreateData();
ManagedSkeletonLoader.SetData(asset, replacement);
Check(!ReferenceEquals(stateData, asset.GetAnimationStateData()), "Replacing a skeleton must replace stale animation state");
Check(ReferenceEquals(asset.GetAnimationStateData().SkeletonData, replacement), "Replacement state must reference replacement skeleton");
Console.WriteLine($"Passed {checks} Spine initialization regression checks against the game's Spine DLLs");
