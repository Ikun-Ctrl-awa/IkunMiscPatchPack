using MegaCrit.Sts2.Core.GameActions.Multiplayer;
namespace YuiExtra.LocalCompatibility;
public static class ChoiceContextFix
{
    public static PlayerChoiceContext Resolve(PlayerChoiceContext? explicitContext, PlayerChoiceContext? parentContext)
    {
        // 沿用可选牌的真实动作/上层上下文；无可用上下文时等待选择完成再继续。
        if (explicitContext != null && explicitContext is not ThrowingPlayerChoiceContext) return explicitContext;
        if (parentContext != null && parentContext is not ThrowingPlayerChoiceContext) return parentContext;
        return new BlockingPlayerChoiceContext();
    }
}
