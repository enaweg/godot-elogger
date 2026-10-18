using System.Threading;
using GdUnit4;
using static GdUnit4.Assertions;

namespace Enaweg.Logger.Tests;

[TestSuite]
public class GodotLogGuardTest
{
    [TestCase]
    public void Enter_HoldsTheGuardUntilDisposed()
    {
        AssertBool(GodotLogGuard.IsWriting).IsFalse();

        using (GodotLogGuard.Enter())
        {
            AssertBool(GodotLogGuard.IsWriting).IsTrue();
        }

        AssertBool(GodotLogGuard.IsWriting).IsFalse();
    }

    [TestCase]
    public void Enter_Nested_KeepsTheGuardUntilTheOutermostScopeEnds()
    {
        // A processor can log while another one is writing (e.g. a sink that itself logs); leaving the inner
        // scope must not release the outer one.
        using (GodotLogGuard.Enter())
        {
            using (GodotLogGuard.Enter())
            {
                AssertBool(GodotLogGuard.IsWriting).IsTrue();
            }

            AssertBool(GodotLogGuard.IsWriting).IsTrue();
        }

        AssertBool(GodotLogGuard.IsWriting).IsFalse();
    }

    [TestCase]
    public void Enter_OnlyAffectsTheCurrentThread()
    {
        using (GodotLogGuard.Enter())
        {
            // Engine messages raised on other threads while this one writes are genuine and must be captured.
            // A dedicated thread: waiting on Task.Run can inline the task onto this thread.
            var otherThreadWriting = true;
            var thread = new Thread(() => otherThreadWriting = GodotLogGuard.IsWriting);
            thread.Start();
            thread.Join();

            AssertBool(otherThreadWriting).IsFalse();
        }
    }
}
