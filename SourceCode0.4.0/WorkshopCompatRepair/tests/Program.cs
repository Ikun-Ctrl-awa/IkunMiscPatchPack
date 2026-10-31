using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;

string root=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../.."));
string game=(System.Environment.GetEnvironmentVariable("STS2_GAME_MANAGED_DIR")??throw new InvalidOperationException("请设置 STS2_GAME_MANAGED_DIR"));
var files=Directory.GetFiles(Path.Combine(root,"original"),"*.dll",SearchOption.AllDirectories).Concat(Directory.GetFiles(game,"*.dll")).Concat(Directory.GetFiles(Path.Combine((System.Environment.GetEnvironmentVariable("STS2_WORKSHOP_DIR")??throw new InvalidOperationException("请设置 STS2_WORKSHOP_DIR")),"3737335127/BaseLib"),"*.dll")).ToArray();
files=Directory.GetFiles(Path.Combine((System.Environment.GetEnvironmentVariable("STS2_WORKSHOP_DIR")??throw new InvalidOperationException("请设置 STS2_WORKSHOP_DIR")),"3747602295/compat/0.111.0"),"*.dll").Concat(Directory.GetFiles(Path.Combine((System.Environment.GetEnvironmentVariable("STS2_WORKSHOP_DIR")??throw new InvalidOperationException("请设置 STS2_WORKSHOP_DIR")),"3747602295/shared"),"*.dll")).Concat(files).ToArray();
AssemblyLoadContext.Default.Resolving+=(ctx,n)=>{var p=files.FirstOrDefault(p=>Path.GetFileNameWithoutExtension(p)==n.Name);return p==null?null:ctx.LoadFromAssemblyPath(p);};
bool patched=args.Contains("patched");
string Mod(string name,string original)=>patched?Path.Combine(root,"patched",name,name+".dll"):Path.Combine(root,"original",original,name+".dll");
var fusion=AssemblyLoadContext.Default.LoadFromAssemblyPath(Mod("FusionExpansionPack","3762574884"));
var anthony=AssemblyLoadContext.Default.LoadFromAssemblyPath(Mod("AutoAnthony","3786611028"));
var pig=AssemblyLoadContext.Default.LoadFromAssemblyPath(Mod("YuWanCard.Content","3747566068/lib/0.111.0"));
var dixie=AssemblyLoadContext.Default.LoadFromAssemblyPath(Mod("DixieRelicSort","3779056963"));
Checks.Run(fusion,anthony,pig,dixie,patched);

static class Checks
{
    static int passed,failed;
    static void Check(string name,bool result){Console.WriteLine($"{(result?"PASS":"FAIL")} {name}");if(result)passed++;else failed++;}
    public static void Run(Assembly fusion,Assembly anthony,Assembly pig,Assembly dixie,bool patched)
    {
        MegaCrit.Sts2.Core.TestSupport.TestMode.IsOn=true;
        var type=fusion.GetType("FusionExpansionPack.FusionReward")!;
        var reward=(MegaCrit.Sts2.Core.Rewards.Reward)Activator.CreateInstance(type,new object?[]{null})!;
        var field=type.GetField("FusionRewardType");
        field?.SetValue(null,Enum.ToObject(field.FieldType,171145322));
        var save=reward.ToSerializable();
        Check("Fusion reward uses a distinct save discriminator",(int)save.RewardType>6);
        var baseType=Type.GetType("BaseLib.Abstracts.CustomReward, BaseLib")!;
        Check("Fusion reward participates in BaseLib save registration",baseType.IsAssignableFrom(type));
        var getter=type.GetProperty("DeserializeMethod");
        var factory=(Delegate?)getter?.GetValue(reward);
        var restored=factory?.DynamicInvoke(save,null);
        Check("Saved fusion reward restores the same reward class",restored?.GetType()==type);
        Check("Fusion deserializer does not capture an instance",factory!=null&&factory.Target==null);
        Check("Fusion reward has a constructor for framework registration",type.GetConstructor(Type.EmptyTypes)!=null);
        var alreadyRemoved=(MegaCrit.Sts2.Core.Models.CardModel)RuntimeHelpers.GetUninitializedObject(typeof(MegaCrit.Sts2.Core.Models.Cards.Inflame));
        typeof(MegaCrit.Sts2.Core.Models.AbstractModel).GetProperty("IsMutable",BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic)!.SetValue(alreadyRemoved,true);
        var removal=fusion.GetType("WorkshopLocalFixes.DisassemblyRemovalFix")?.GetMethod("RemoveIfStillInCombat");
        var removalTask=(Task?)removal?.Invoke(null,new object[]{alreadyRemoved,true});
        Check("Disassembly continues after autoplay has removed the power fusion",removalTask?.IsCompletedSuccessfully==true);
        var secondRemoval=(Task?)removal?.Invoke(null,new object[]{alreadyRemoved,false});
        Check("A second removal of an absent fusion remains harmless",secondRemoval?.IsCompletedSuccessfully==true);
        var native=(MegaCrit.Sts2.Core.Models.CardModel)RuntimeHelpers.GetUninitializedObject(typeof(MegaCrit.Sts2.Core.Models.Cards.Clumsy));
        var modded=(MegaCrit.Sts2.Core.Models.CardModel)RuntimeHelpers.GetUninitializedObject(fusion.GetType("FusionExpansionPack.FateFusionCard")!);
        var filter=anthony.GetType("WorkshopLocalFixes.NativePoolFix")?.GetMethod("NativeCards");
        var cards=new[]{native,modded};
        var actual=filter==null?cards:(MegaCrit.Sts2.Core.Models.CardModel[])filter.Invoke(null,new object[]{cards})!;
        Check("Native catalog excludes another mod's Colorless cards",actual.Length==1&&ReferenceEquals(actual[0],native));
        Check("Original input pool remains available to its mod",cards.Length==2&&ReferenceEquals(cards[1],modded));
        const BindingFlags flags=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.Static;
        var player=(MegaCrit.Sts2.Core.Entities.Players.Player)RuntimeHelpers.GetUninitializedObject(typeof(MegaCrit.Sts2.Core.Entities.Players.Player));
        var owner=new MegaCrit.Sts2.Core.Entities.Creatures.Creature(player,80,80);
        var encounter=new MegaCrit.Sts2.Core.Models.Encounters.NibbitsWeak().ToMutable();
        var state=new MegaCrit.Sts2.Core.Combat.CombatState(encounter);
        var room=new MegaCrit.Sts2.Core.Rooms.CombatRoom(state);
        room.MarkPreFinished(); room.AddExtraReward(player,reward);
        var roomSave=room.ToSerializable();
        Check("Finished combat snapshot preserves the fusion reward",roomSave.IsPreFinished&&roomSave.ExtraRewards[player.NetId].Count==1&&(int)roomSave.ExtraRewards[player.NetId][0].RewardType>6);
        if(patched)
        {
            var json=System.Text.Json.JsonSerializer.Serialize(roomSave);
            var fromDisk=System.Text.Json.JsonSerializer.Deserialize<MegaCrit.Sts2.Core.Saves.Runs.SerializableRoom>(json)!;
            var loaded=factory!.DynamicInvoke(fromDisk.ExtraRewards[player.NetId][0],player);
            Check("JSON combat snapshot restores the custom fusion reward",loaded!.GetType()==type);
        }
        player.GetType().GetField("<Creature>k__BackingField",flags)!.SetValue(player,owner);
        var monster=(MegaCrit.Sts2.Core.Models.MonsterModel)RuntimeHelpers.GetUninitializedObject(pig.GetType("YuWanCard.Monsters.PigMinion")!);
        typeof(MegaCrit.Sts2.Core.Models.AbstractModel).GetProperty("IsMutable",flags)!.SetValue(monster,true);
        var pet=new MegaCrit.Sts2.Core.Entities.Creatures.Creature(monster,MegaCrit.Sts2.Core.Combat.CombatSide.Player,null){PetOwner=player};
        var regen=(MegaCrit.Sts2.Core.Models.Powers.RegenPower)RuntimeHelpers.GetUninitializedObject(typeof(MegaCrit.Sts2.Core.Models.Powers.RegenPower));
        typeof(MegaCrit.Sts2.Core.Models.AbstractModel).GetProperty("IsMutable",flags)!.SetValue(regen,true);
        typeof(MegaCrit.Sts2.Core.Models.PowerModel).GetField("_owner",flags)!.SetValue(regen,pet);
        var prefix=pig.GetType("WorkshopLocalFixes.PigMinionRegenFix")?.GetMethod("Prefix");
        IEnumerable<MegaCrit.Sts2.Core.Entities.Creatures.Creature> Apply(params MegaCrit.Sts2.Core.Entities.Creatures.Creature[] participants){object[] input={regen,participants};prefix?.Invoke(null,input);return (IEnumerable<MegaCrit.Sts2.Core.Entities.Creatures.Creature>)input[1];}
        Check("Summoned pig participates in its owner's Regen tick",Apply(owner).Contains(pet));
        Check("Existing participant does not get duplicated",Apply(owner,pet).Count(c=>ReferenceEquals(c,pet))==1);
        Check("Another player's extra turn does not trigger this pig",!Apply(new MegaCrit.Sts2.Core.Entities.Creatures.Creature(player,80,80)).Contains(pet));
        typeof(MegaCrit.Sts2.Core.Entities.Creatures.Creature).GetField("_currentHp",flags)!.SetValue(pet,0);
        Check("Dead pig is not revived by Regen",!Apply(owner).Contains(pet));
        typeof(MegaCrit.Sts2.Core.Models.PowerModel).GetField("_owner",flags)!.SetValue(regen,owner);
        Check("Native player Regen participant list remains intact",Apply(owner).SequenceEqual(new[]{owner}));
        var normalizer=dixie.GetType("WorkshopLocalFixes.HotkeyNameFix")?.GetMethod("Normalize");
        string Normalize(string s)=>(string?)normalizer?.Invoke(null,new object[]{s})??s;
        Check("Legacy numpad plus binding converts to the Godot key name",Normalize("KeypadAdd")=="KpAdd");
        Check("Unrelated custom hotkey remains unchanged",Normalize("Control+F8")=="Control+F8");
        Console.WriteLine($"REGRESSION_RESULT passed={passed} failed={failed}");Environment.ExitCode=failed==0?0:2;
    }
}
