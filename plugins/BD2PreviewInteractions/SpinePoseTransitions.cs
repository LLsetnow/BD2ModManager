using Spine;

namespace BD2PreviewInteractions;

internal static class SpinePoseTransitions
{
    internal static TrackEntry StartMotion(AnimationState state, Skeleton skeleton, string motion)
    {
        // Higher tracks keep their last frame after a non-looping reaction ends.
        // Remove them before the transition can reveal the next pose's parts.
        state.ClearTrack(1);
        state.ClearTrack(2);
        skeleton.SetToSetupPose();
        state.Apply(skeleton);
        var entry = state.SetAnimation(0, motion, false);
        entry.MixDuration = 0;
        ApplyCurrent(state, skeleton);
        return entry;
    }

    internal static void ApplyCurrent(AnimationState state, Skeleton skeleton)
    {
        state.Apply(skeleton);
        skeleton.UpdateWorldTransform();
    }
}
