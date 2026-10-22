using MegaCrit.Sts2.Core.Modding;
namespace AikunMiscPatchPack;

public sealed record PatchOption(string Key,string Title,string Description,string[] Required,string[]? AnyOf=null,bool Misc=false);
public static class Options {
 public static readonly PatchOption[] All={
  new("better_topbar","更好的顶栏","图标过多时自动换行，扩展原版石质背景；遗物栏、遗物折叠按钮及右上版本、种子、HASH 随顶栏下移。海克斯选择面板避开多行顶栏，空间不足时等比缩放。顶栏提示若超出右边缘，整组改放鼠标左侧，并校正屏幕边界。",[],Misc:true),
  new("skip_story","剧情可以跳过","卡牌或效果正在结算时，按快捷键跳过当前结算链的动画等待，直接推进结果。默认空格，可改键；所有伤害、充能球和触发仍逐次结算。选牌、暂停和菜单期间不响应。",[],Misc:true),
  new("mod_form","mod形态","新增 3 费无色稀有能力牌：打出时和你的回合开始时，生成随机模组的攻击、技能或能力牌；本回合免费，获得虚无和消耗。升级后生成升级牌，多次升级按次数传递。默认排除自身，没有其他合格模组卡时不进入无色卡池；可在下方开启自身候选。",[],Misc:true),
  new("multi_enchant_icons","多重附魔图标优化","展开更多附魔的附加槽位与 Plague 等复合附魔，按固定顺序显示全部实际附魔的图标、层数和启用状态。",[]),
  new("power_corner_labels","状态图标：附加计数显示","修复状态图标附加计数被放大成下方巨大数字的问题，字号跟随原生状态层数。飞向粉色的明天的剩余攻击次数显示在左上角，保留右下角效果层数；不改伤害或计数规则。",[]),
  new("pink_tomorrow_damage","昔涟：飞向粉色的明天增伤","适配当前游戏的伤害倍率接口，修复剩余次数正常显示但实际攻击没有翻倍的问题。沿用原效果的来源、每回合次数及多段攻击规则；原模组已适配时不重复加成。",["Medo"]),
  new("medo_events_api","昔涟：事件与存档接口兼容","适配逐火英雄纪与往世乐土的扣血接口，恢复选项结算；让昔涟任务延后标记和遗物触发记录按当前存档属性保存，避免读档丢失。保留生命代价、奖励与触发规则。",["Medo"]),
  new("columbina_portraits","哥伦比娅：百科卡图","百科、普通预览与测试图缺少小图时，使用模组自带的对应大图，修复所有卡牌显示成笑脸占位图的问题。已有专属小图、测试图时保留原图。",["ColumbinaMod"]),
  new("enchantment_localization","附魔图鉴：汉化与描述补全","补充乘胜追击、临时播种及复合附魔缺少的中文名称和效果说明，同时补齐相应卡面附加文字。只在简体中文下补全缺失文案，保留原模组已有中文及附魔效果。",[]),
  new("orb_channel_context","东尼算法：充能球导致无法出牌","修复其他模组接管充能球生成时，东尼算法清理未创建的上下文，导致回合开始结算中断、所有手牌无法打出的问题。兼容缺陷角色的通电效果，保留充能、激发和伤害规则。",["AutoAnthony"]),
  new("upgrade_preview","多次升级卡牌：升级预览","从卡牌当前等级重新生成下一次升级预览，修复旧缓存显示错误等级的问题。详情页未勾选时显示当前牌，勾选后显示下一级；实际升级次数与效果不变。",[]),
  new("fusion_upgrades","火堆融合：强化等级与升级资格","融合牌强化等级等于所有素材的等级之和；每次按融合顺序升级首个仍可升级的素材。普通素材升满后跳过，保留素材自身及多次升级附魔的升级能力；所有素材均无法升级时，融合牌也无法升级。不可升级的诅咒等素材不会获得无效升级。",["CardFusion"]),
  new("act4_key_topbar","Act 4 Heart：钥匙顶栏","红、绿、蓝钥匙在顶栏钥匙框的固定位置显示；遗物栏隐藏钥匙图标，保留原模组的钥匙存档、获取和开门逻辑。",["Act4Heart"]),
  new("past_rooms","往昔之章：随机混入","保留原版章节，按概率混入对应章节的一代普通战斗、精英、事件与 Boss。默认各 30%，可分别调整；Boss 在地图生成时确定，读档保持结果。",["ActsFromThePast"]),
  new("columbina_api","哥伦比娅：测试版接口兼容","适配攻击、消耗、失去格挡及增伤接口，修正卡图路径；解决攻击牌悬停、结算中断等问题。",["ColumbinaMod"]),
  new("moon_gleam","哥伦比娅：月兆满辉减费","只在实际能减费的非 X 费牌中随机选牌，避免零费和不可打出的牌吞掉效果。",["ColumbinaMod"]),
  new("bane_ethereal","更好的进阶：虚无词条","修复进阶之灾规则误删所有卡牌虚无、并阻止之后添加虚无的问题。",["BetterAscension"]),
  new("act_attrition","更好的进阶：读档扣最大生命","换幕扣 6 最大生命只在真正换幕时触发，读档不再重复扣除。",["BetterAscension"]),
  new("chest_key","遗物三选一 × Act 4 Heart：宝箱钥匙","真正跳过宝箱遗物奖励时补发蓝宝石钥匙；仅关闭选牌界面不会发钥匙，已有钥匙不重复发放。",["RelicRewardChoices","Act4Heart"]),
  new("double_vision_relic_choice","海克斯复视 × 遗物三选一","领取遗物三选一选中的遗物时，按复视原有规则额外获得副本；保留蜡制状态和海克斯同步流程。取消、跳过或已领取奖励不会额外发放。",["HextechRunes","RelicRewardChoices"]),
  new("gravity_toggle","Gravity：角色选择开关","角色头像分页过低时，将开关移回可见区域，保留原有点击和保存逻辑。",["Gravity"]),
  new("fusion_rewards","融合扩展包：战后奖励与读档","为融合奖励使用独立存档类型，在战后快照前登记奖励；空候选池不再中断结算。",["FusionExpansionPack","CardFusion"]),
  new("super_disassembly","融合扩展包：超级拆解","避免重复移除已离开战斗的融合牌，使返手、自身消耗及消耗堆结算继续完成。",["FusionExpansionPack","CardFusion"]),
  new("anthony_pool","东尼算法：第三方角色兼容","原版无色牌目录只统计游戏本体卡牌，修复与新三国等模组同时使用时卡在涅奥的问题。",["AutoAnthony"]),
  new("pig_regen","YuWanCard：召唤小猪再生","让猪朋猪友召唤的活着的小猪在其主人回合正确结算再生，保留死亡和额外回合规则。",["YuWanCard"]),
  new("curse_scroll","千咒卷轴 × 更好的进阶：Boss 诅咒","修复进阶 20 在 Boss 房直接加入牌组的诅咒绕过千咒卷轴的问题；进房完成前调用卷轴原有消咒处理，保留遗物效果和次数限制。仅处理本次新增诅咒，不移除旧诅咒。",["YuWanCard","BetterAscension"]),
  new("ring_boss_hp","七咒之戒 × 海克斯：Boss 最大生命惩罚","修复戒指按面板生命算出的 25% 扣除量，被海克斯血量锻造器等再次放大的问题。改按基础生命计算，由海克斯换算一次，最终按原整数生命规则取整；保留戒指其余效果和原生结算，不自动补回旧存档已损失的生命。",["YuWanCard","HextechRunes"]),
  new("dixie_hotkey","遗物整理：小键盘加号快捷键","将旧配置 KeypadAdd 规范为 KpAdd，修复快捷键解析失败，并保留组合键。",["DixieRelicSort"]),
  new("plague_api","Plague：初始化与测试版兼容","移除已删除的属性缓存注入，适配伤害与选牌接口，解决 DLL 初始化失败。",["plague"]),
  new("fusion_enchants","火堆融合：更多附魔继承","融合时继承更多附魔的全部词条，兼容已有融合牌的子卡附魔。",["CardFusion"],["MoreEnchantments","MoreEnchantStandalone"]),
  new("yui_replay","YUI：战斗无法结算","遗物变形或离开玩家后停止错误重放，修复石中剑变形后的结算卡住，保留正常重复触发。",["YUICore"]),
  new("yui_choice","YUI：回合结束卡住","为遗物与获得能量的触发链传递有效选牌上下文，修复抽牌触发预见等流程卡住。",["YUICore"]),
  new("yui_ignite","YUI：点火附魔结算与保留","永久牌组上的点火附魔不再被战后清理删除，下一场仍可触发；战斗内临时附魔保留原清理规则。适配魂焰的附加卡牌预览。生成魂焰需要原作者的 YUI The Spire Expansion - Cards 卡牌扩展。",["YUICore"]),
  new("fusion_blessing","火堆融合：赐福词条继承","让融合牌动态继承 YUI 赐福词条，例如速行者获得的奇巧。",["CardFusion","YUICore"]),
  new("fusion_quest","融合牌：迷迷与昔涟任务","任务事件识别融合牌内的任务卡，完成任务后移除对应融合牌，避免问号房重复出现任务。",["CardFusion","Medo"])
 };
 static readonly Dictionary<string,string> Names=new(){["ColumbinaMod"]="哥伦比娅",["BetterAscension"]="更好的进阶",["RelicRewardChoices"]="遗物奖励三选一",["Act4Heart"]="Act 4 Heart",["CardFusion"]="卡牌融合",["FusionExpansionPack"]="融合卡牌扩展包",["AutoAnthony"]="东尼算法",["YuWanCard"]="YuWanCard",["DixieRelicSort"]="遗物隐藏与整理",["plague"]="Plague",["YUICore"]="YUICore",["Medo"]="昔涟",["MoreEnchantments"]="更多附魔",["MoreEnchantStandalone"]="更多附魔独立版"};
 public static string Display(string id)=>id=="HextechRunes"?"海克斯大乱斗":Names.GetValueOrDefault(id,id);
 public static bool Present(string id,bool beforeLoad=false)=>ModManager.Mods.Any(m=>m.manifest?.id==id&&(m.state==ModLoadState.Loaded||(beforeLoad&&m.state==ModLoadState.None)));
 public static string[] Missing(PatchOption option,bool beforeLoad=false){var missing=option.Required.Where(id=>!Present(id,beforeLoad)).Select(Display).ToList();if(option.AnyOf is {Length:>0}&& !option.AnyOf.Any(id=>Present(id,beforeLoad)))missing.Add(string.Join(" / ",option.AnyOf.Select(Display)));return missing.ToArray();}
 public static bool Eligible(string key)=>Missing(All.Single(o=>o.Key==key),true).Length==0;
}
