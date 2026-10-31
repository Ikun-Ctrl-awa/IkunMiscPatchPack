using MegaCrit.Sts2.Core.Events;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;

namespace AikunMiscPatchPack;
public static class SectEventOptions {
 public const string UnavailableKey="AIKUN_MISC_PATCH_PACK.optionUnavailable";
 public static EventOption Create(EventModel model,string key,bool allowed,Func<Task> choose,IEnumerable<IHoverTip> tips)=>
  new(model,allowed?choose:null,model.GetOptionTitle(key)!,allowed?model.GetOptionDescription(key)!:new LocString("events",UnavailableKey),key,tips);
 public static CardHoverTip UpgradedTip<T>() where T:CardModel{
  var card=ModelDb.Card<T>().ToMutable();card.UpgradeInternal();card.FinalizeUpgradeInternal();return new CardHoverTip(card);
 }
}
