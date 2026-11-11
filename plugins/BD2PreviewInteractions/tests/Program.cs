using System;
using BD2PreviewInteractions;

int checks = 0;
void Check(bool condition, string message) { checks++; if (!condition) throw new Exception(message); }
var input = new PointerGestureState();
Check(!input.Begin(2, -3, 1), "Middle button cannot own input or pan");
Check(input.Begin(0, -1, 1), "Left press accepted");
Check(input.ConsumeTap(), "One tap per physical press");
Check(!input.Begin(0, -1, 1.01) && !input.ConsumeTap(), "Duplicate pointer-down cannot add taps");
Check(!input.End(1, -2) && input.Owns(0, -1), "Right release cannot finish left gesture");
Check(!input.TryStartDrag(-1, 1.2, .35, 10000), "Early mouse motion cannot drag");
Check(!input.TryStartDrag(-1, 1.4, .35, 16), "Held stationary pointer cannot start drag");
Check(input.TryStartDrag(-1, 1.4, .35, 25), "Long-held left can start drag");
Check(input.TryStartDrag(-1, 1.5, .35, 0), "Once started, drag remains owned until release");
Check(input.CompleteHoldOnce() && !input.CompleteHoldOnce(), "Hold succeeds once even before release");
Check(input.End(0, -1) && !input.End(0, -1), "PointerUp plus EndDrag cannot repeat release");
Check(!input.CompleteHoldOnce(), "Released gesture cannot succeed again");
Check(input.Begin(1, -2, 2), "Right press accepted");
Check(!input.ConsumeTap() && !input.TryStartDrag(-2, 3, .35, 100), "Right press never touches/charges character");
Check(input.End(1, -2), "Right pan ends normally");
Check(input.Begin(0, -1, 3) && input.ConsumeTap(), "New physical click can tap again");
input.Cancel();
Check(!input.End(0, -1) && !input.CompleteHoldOnce(), "Pose/reset cancellation prevents stale release effects");
Check(input.Begin(0, -1, 4) && input.ConsumeTap(), "Next pose accepts a fresh gesture");
Console.WriteLine($"Passed {checks} gesture regression checks");
