namespace OpenScctGame.Smoke;

/// <summary>
/// Always fails. CI runs it to prove that a failing smoke test gives a non-zero exit code
/// through the console wrapper and the exported exe.
/// </summary>
public sealed class SelfTestFailSmoke : SmokeTest
{
    public const string TestName = "selftest-fail";

    public override string Name => TestName;

    public override SmokeResult Step(SmokeContext context) =>
        context.Frame < 3 ? SmokeResult.Running : SmokeResult.Fail("expected failure");
}
