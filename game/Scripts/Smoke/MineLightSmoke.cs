using Godot;
using OpenScct.Server;
using OpenScctGame.Fx;

namespace OpenScctGame.Smoke;

public sealed class MineLightSmoke : SmokeTest
{
    public const string TestName = "mine-light";
    public override string Name => TestName;
    public override SmokeResult Step(SmokeContext context)
    {
        if (ContentPaths.Shared is not { } root) return SmokeResult.Fail("no shared content");
        var library = FxLibrary.Load(root);
        var view = new EffectViews { Library = library };
        context.Tree.Root.AddChild(view);
        const string on = "SGameplayObjectsTex.Object.proxy_mine_On";
        const string off = "SGameplayObjectsTex.Object.proxy_mine_Off_final";
        if (library.Mesh("SGameplayObjects.Mines.proxy_mine") is not { } mesh || mesh.GetSurfaceCount() < 2)
            return SmokeResult.Fail("mine light surface missing");
        if (view.SkinMaterial(on, 0) is not StandardMaterial3D material || material.AlbedoTexture is null)
            return SmokeResult.Fail("armed light material missing");
        Vector3 first = material.Uv1Offset;
        view.SkinMaterial(on, 0.5f);
        if (material.Uv1Offset == first) return SmokeResult.Fail("light did not blink after 0.5 seconds");
        view.SkinMaterial(on, 1f);
        if (material.Uv1Offset != first) return SmokeResult.Fail("light did not loop after one second");
        if (view.SkinMaterial(off, 0) is not StandardMaterial3D dark || dark == material || library.AnimationForSkin(off) is not null)
            return SmokeResult.Fail("chaffed light must remain off");
        return SmokeResult.Pass("mine light surface, armed 2-frame/2-FPS blink, and static chaffed skin");
    }
}
