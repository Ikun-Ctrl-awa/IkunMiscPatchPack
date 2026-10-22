using Godot;
using MegaCrit.Sts2.Core.Modding;
namespace AikunMiscPatchPack;
[ModInitializer(nameof(Initialize))]
public static class Entry {
 public static void Initialize(){
  Settings.Initialize();RuntimeLoader.Initialize();
  ModFormContent.Initialize();
  if(Settings.Active("better_topbar")){AdaptiveTopBar.Entry.Initialize();RuntimeLoader.States["better_topbar"]="已应用。";}
  ((SceneTree)Engine.GetMainLoop()).CreateTimer(1).Timeout+=AfterMods;
  GD.Print("[AikunMiscPatchPack] initialized; optional target mods; restart-applied settings");
 }
 static void AfterMods(){
  if(ModManager.State!=ModManagerState.Initialized){((SceneTree)Engine.GetMainLoop()).CreateTimer(.5).Timeout+=AfterMods;return;}
  try{ModFormContent.AfterMods();if(Settings.Active("mod_form"))RuntimeLoader.States["mod_form"]="已应用。";}
  catch(Exception e){RuntimeLoader.States["mod_form"]="未应用："+e.Message;GD.Print("[AikunMiscPatchPack] mod_form skipped: "+e);}
  Install("multi_enchant_icons",MultiEnchantIcons.Install);
  Install("power_corner_labels",PowerCornerLabelFix.Install);
  Install("pink_tomorrow_damage",PinkTomorrowDamageFix.Install);
  Install("columbina_portraits",ColumbinaPortraitFix.Install);
  Install("enchantment_localization",EnchantmentLocalizationFix.Install);
  Install("yui_ignite",IgniteEnchantmentFix.Install);
  Install("orb_channel_context",OrbChannelContextFix.Install);
  Install("upgrade_preview",UpgradePreviewFix.Install);
  Install("fusion_upgrades",FusionUpgradeFix.Install);
  Install("curse_scroll",ThousandCurseScrollFix.Install);
  Install("ring_boss_hp",RingBossHpFix.Install);
  Install("double_vision_relic_choice",DoubleVisionRelicChoiceFix.Install);
  Install("skip_story",SkipStory.Install);
  Install("act4_key_topbar",HeartKeyTopBar.Install);
  Install("past_rooms",PastRooms.Install);
  if(new[]{"fusion_blessing","fusion_quest"}.Any(k=>Settings.Active(k)&&Options.Eligible(k)))FusionQuestBlessingFix.Entry.Install();
  foreach(var key in new[]{"fusion_blessing","fusion_quest"})if(Settings.Active(key)&&Options.Eligible(key))RuntimeLoader.States[key]="已应用。";
  Settings.RegisterPages();
 }
 static void Install(string key,Action action){
  if(!Settings.Active(key)||!Options.Eligible(key))return;
  try{action();RuntimeLoader.States[key]="已应用。";}
  catch(Exception e){RuntimeLoader.States[key]="补丁未应用："+e.Message;GD.Print("[AikunMiscPatchPack] "+key+" skipped: "+e);}
 }
}
