using System.Text.Json;
using Godot;
using STS2RitsuLib.Settings;
using Pack=AikunMiscPatchPack;

namespace Aikun020Probe;
public static partial class Entry {
 static string CompactVisibleText(string text)=>new(text.Where(c=>!char.IsWhiteSpace(c)&&char.GetUnicodeCategory(c)!=System.Globalization.UnicodeCategory.Format).ToArray());
 static async Task SettingsCompactChecks(){
  string before=JsonSerializer.Serialize(Pack.Settings.Current);
  var pages=ModSettingsRegistry.GetPages().Where(p=>p.ModId==Pack.Settings.Id&&p.Id!=Pack.Settings.Id).ToArray();
  foreach(var page in pages){
   Check("compact native open "+page.Id,ModSettingsNavigator.RequestOpenByIds(Pack.Settings.Id,page.Id,page.Sections.First().Id,null).Success);await Wait(2);
   var roots=SettingsNodes(Tree.Root).OfType<Control>().Where(n=>n.Name.ToString().StartsWith("AikunDescription_")&&n.IsVisibleInTree()).ToArray();
   Check("compact all native section rows built "+page.Id,roots.Length==page.Sections.Count);
   foreach(var root in roots){
    string key=root.Name.ToString()["AikunDescription_".Length..];var option=Pack.Options.All.Single(o=>o.Key==key);
    var button=(Button)root.GetMeta("expand_button").AsGodotObject();var line=(Control)button.GetParent();var enabled=(Control)line.GetChild(0);
    var labels=SettingsNodes(enabled).OfType<RichTextLabel>().Where(l=>l.IsVisibleInTree()).ToArray();
    File.AppendAllText(Output+"/compact-native-labels.jsonl",JsonSerializer.Serialize(new{key,Expected=option.Title,Actual=labels.Select(l=>new{l.Text,Parsed=l.GetParsedText()})})+"\n");
    var label=labels.Single(l=>CompactVisibleText(l.GetParsedText())==CompactVisibleText(option.Title));
    Check("compact title shares enabled row "+key,label.IsVisibleInTree()&&Math.Abs(label.GetGlobalRect().GetCenter().Y-button.GetGlobalRect().GetCenter().Y)<4);
    Check("compact native toggle retained "+key,SettingsNodes(enabled).OfType<Control>().Any(c=>c.GetType().Name=="ModSettingsToggleControl"&&c.IsVisibleInTree()));
    Check("compact duplicate heading hidden "+key,!((Control)root.GetParent().GetChild(0)).Visible);
    Check("compact section menu retained "+key,line.GetChildCount()==3&&line.GetChild(1).GetType().Name=="ModSettingsActionsButton");
   }
   Tree.Root.GetTexture().GetImage().SavePng(Output+"/settings-compact-"+(page.Id.EndsWith("misc")?"misc":"patches")+".png");
   var first=roots[0];var expand=(Button)first.GetMeta("expand_button").AsGodotObject();var body=first.GetNode<Control>("Body");
   await ClickSettings(expand);await Wait(.3);Check("compact mouse expands "+page.Id,body.IsVisibleInTree());
   await ClickSettings(expand);await Wait(.3);Check("compact mouse collapses "+page.Id,!body.Visible);
  }
  Check("compact keeps settings values",JsonSerializer.Serialize(Pack.Settings.Current)==before);
 }
}
