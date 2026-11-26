using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace GizmoFolders {
 public sealed class Folder : IExposable {
  public string name="Magic";
  public List<string> keys=new List<string>();
  public void ExposeData(){Scribe_Values.Look(ref name,"name","Magic");Scribe_Collections.Look(ref keys,"keys",LookMode.Value);if(keys==null)keys=new List<string>();}
 }
 public sealed class Layout : IExposable {
  public Pawn pawn;
  public List<Folder> folders=new List<Folder>();
  public void ExposeData(){Scribe_References.Look(ref pawn,"pawn");Scribe_Collections.Look(ref folders,"folders",LookMode.Deep);if(folders==null)folders=new List<Folder>();}
  public Folder Owner(string key)=>folders.FirstOrDefault(f=>f.keys.Contains(key));
  public void Move(string key,Folder destination){foreach(var f in folders)f.keys.RemoveAll(k=>k==key);if(destination!=null)destination.keys.Add(key);}
 }
 public sealed class Store : GameComponent {
  public List<Layout> layouts=new List<Layout>();
  public Store(Game game){}
  public override void ExposeData(){Scribe_Collections.Look(ref layouts,"gizmoFoldersLayouts",LookMode.Deep);if(layouts==null)layouts=new List<Layout>();if(Scribe.mode==LoadSaveMode.PostLoadInit)layouts.RemoveAll(l=>l==null||l.pawn==null);}
  public static Store CurrentStore=>Current.Game?.GetComponent<Store>();
  public Layout Get(Pawn p,bool create=false){var l=layouts.FirstOrDefault(x=>x.pawn==p);if(l==null&&create){l=new Layout{pawn=p};layouts.Add(l);}return l;}
 }
 [StaticConstructorOnStartup] public static class Hooks {
  [ThreadStatic] static bool raw;
  static Texture2D folderIcon;
  static Hooks(){new Harmony("domenklay.gizmofolders").Patch(AccessTools.Method(typeof(Pawn),"GetGizmos"),postfix:new HarmonyMethod(typeof(Hooks),nameof(Postfix)){priority=Priority.Last});}
  public static bool Selected(Pawn p)=>p!=null&&!p.Destroyed&&!p.Dead&&p.Faction==Faction.OfPlayer&&Find.Selector?.NumSelected==1&&Find.Selector.SingleSelectedThing==p;
  public static List<Gizmo> Raw(Pawn p){bool old=raw;raw=true;try{return p.GetGizmos().Where(g=>g!=null).ToList();}finally{raw=old;}}
  public static string Key(Command c){
   if(c==null||c is FolderButton)return null;
   // Native and VEF abilities use defName, independent of language or cooldown.
   if(c is RimWorld.Command_Ability native && native.Ability?.def!=null)return "ability:"+native.Ability.def.defName;
   var type=c.GetType();
   if(type.FullName.StartsWith("VEF.Abilities.",StringComparison.Ordinal)||type.FullName.Contains("Ability")||type.FullName.Contains("Psycast")){
    try{var ability=AccessTools.Field(type,"ability")?.GetValue(c)??AccessTools.Property(type,"Ability")?.GetValue(c,null);if(ability!=null){var d=AccessTools.Field(ability.GetType(),"def")?.GetValue(ability)??AccessTools.Property(ability.GetType(),"Def")?.GetValue(ability,null);if(d is Def def)return "modability:"+type.FullName+":"+def.defName;}}catch{}
   }
   string label=c.Label??c.defaultLabel;
   if(string.IsNullOrWhiteSpace(label))return null;
   return "command:"+type.FullName+":"+c.groupKey+":"+label;
  }
  public static Dictionary<string,Command> Available(IEnumerable<Gizmo> items){
   return items.OfType<Command>().Where(c=>c.Visible&&!(c is FolderButton)).Select(c=>new {c,key=Key(c)}).Where(x=>x.key!=null).GroupBy(x=>x.key).Where(g=>g.Count()==1).ToDictionary(g=>g.Key,g=>g.First().c);
  }
  public static void Postfix(Pawn __instance,ref IEnumerable<Gizmo> __result){
   if(raw||__result==null||!Selected(__instance))return;
   var original=__result.ToList();
   try{
    var store=Store.CurrentStore;if(store==null)return;
    var l=store.Get(__instance);
    var available=Available(original);
    if(available.Count==0&&(l==null||l.folders.Count==0))return;
    var grouped=new Dictionary<Folder,List<Command>>();
    if(l!=null)foreach(var f in l.folders){var list=f.keys.Distinct().Where(available.ContainsKey).Select(k=>available[k]).ToList();if(list.Count>0)grouped[f]=list;}
    var hide=new HashSet<Gizmo>(grouped.Values.SelectMany(x=>x).Cast<Gizmo>());
    var result=original.Where(g=>!hide.Contains(g)).ToList();
    foreach(var pair in grouped)result.Add(new FolderButton(__instance,pair.Key,pair.Value.Count,pair.Value.Min(c=>c.Order)));
    if(grouped.Count==0)result.Add(new FolderButton(__instance,null,0,100f));
    __result=result;
   }catch(Exception e){Log.WarningOnce("[GizmoFolders] "+e,8377201);__result=original;}
  }
  public static Texture2D Icon{
   get{
    if(folderIcon!=null)return folderIcon;
    folderIcon=new Texture2D(64,64,TextureFormat.RGBA32,false);
    var pixels=new Color32[64*64];
    for(int y=0;y<64;y++)for(int x=0;x<64;x++){
     bool back=x>=5&&x<59&&y>=10&&y<48||x>=8&&x<30&&y>=46&&y<55;
     bool front=x>=5&&x<59&&y>=9&&y<43;
     pixels[y*64+x]=front?new Color32(220,165,73,255):back?new Color32(154,107,48,255):new Color32(0,0,0,0);
    }
    folderIcon.SetPixels32(pixels);folderIcon.Apply(false,true);return folderIcon;
   }
  }
  public static void Manage(Pawn p){if(!Selected(p))return;var l=Store.CurrentStore?.Get(p,true);if(l!=null)Find.WindowStack.Add(new Manager(p,l));}
 }
 public sealed class FolderButton : Command_Action {
  readonly Pawn pawn;readonly Folder folder;readonly int count;
  public FolderButton(Pawn p,Folder f,int n,float order){pawn=p;folder=f;count=n;Order=order;groupable=false;shrinkable=false;icon=Hooks.Icon;defaultLabel=f?.name??"GF_Manage".Translate().ToString();defaultDesc=(f==null?"GF_ManageTip":"GF_FolderTip").Translate();action=()=>{if(!Hooks.Selected(pawn))return;if(folder==null)Hooks.Manage(pawn);else Find.WindowStack.Add(new FolderWindow(pawn,folder));};}
  public override string TopRightLabel=>folder==null?null:count.ToString();
  public override IEnumerable<FloatMenuOption> RightClickFloatMenuOptions{get{yield return new FloatMenuOption("GF_Manage".Translate(),()=>Hooks.Manage(pawn));}}
 }
 public sealed class Manager : Window {
  readonly Pawn pawn;readonly Layout layout;Vector2 scroll;string newName="";Folder chosen;string rename="";
  public Manager(Pawn p,Layout l){pawn=p;layout=l;doCloseX=true;closeOnAccept=false;closeOnClickedOutside=false;absorbInputAroundWindow=true;}
  public override Vector2 InitialSize=>new Vector2(Math.Min(690f,UI.screenWidth-40f),Math.Min(620f,UI.screenHeight-50f));
  public override void DoWindowContents(Rect r){
   if(!Hooks.Selected(pawn)){Close(false);return;}
   Text.Font=GameFont.Medium;Widgets.Label(new Rect(0,0,r.width-30,34),"GF_Title".Translate());Text.Font=GameFont.Small;
   Widgets.Label(new Rect(0,35,r.width,44),"GF_Help".Translate());
   float button=112f;
   newName=Widgets.TextField(new Rect(0,84,r.width-button-7,30),newName);
   if(Widgets.ButtonText(new Rect(r.width-button,84,button,30),"GF_Create".Translate())&&!string.IsNullOrWhiteSpace(newName)){
    string name=newName.Trim();if(name.Length>32)name=name.Substring(0,32);chosen=new Folder{name=name};layout.folders.Add(chosen);rename=name;newName="";
   }
   if(chosen!=null&&!layout.folders.Contains(chosen))chosen=null;
   if(chosen==null&&layout.folders.Count>0){chosen=layout.folders[0];rename=chosen.name;}
   if(Widgets.ButtonText(new Rect(0,120,r.width*.45f,30),chosen?.name??"GF_SelectFolder".Translate().ToString())){
    var opts=layout.folders.Select(f=>new FloatMenuOption(f.name,()=>{chosen=f;rename=f.name;})).ToList();if(opts.Count>0)Find.WindowStack.Add(new FloatMenu(opts));
   }
   if(chosen!=null){
    float start=r.width*.45f+5;float remaining=r.width-start;
    rename=Widgets.TextField(new Rect(start,120,Math.Max(20,remaining-115),30),rename);
    if(Widgets.ButtonText(new Rect(r.width-110,120,54,30),"GF_Rename".Translate())&&!string.IsNullOrWhiteSpace(rename)){chosen.name=rename.Trim().Substring(0,Math.Min(32,rename.Trim().Length));}
    if(Widgets.ButtonText(new Rect(r.width-53,120,53,30),"GF_Delete".Translate())){layout.folders.Remove(chosen);chosen=null;rename="";}
   }
   var available=Hooks.Available(Hooks.Raw(pawn)).OrderBy(x=>x.Value.Label).ToList();
   var view=new Rect(0,161,r.width,Math.Max(50,r.height-161));
   var inner=new Rect(0,0,view.width-18,Math.Max(view.height-1,available.Count*42f+4));
   Widgets.BeginScrollView(view,ref scroll,inner);
   try{
    for(int i=0;i<available.Count;i++){
     var pair=available[i];var c=pair.Value;var row=new Rect(0,i*42f,inner.width,40f);
     if(i%2==0)Widgets.DrawBoxSolid(row,new Color(1f,1f,1f,.045f));
     if(c.icon!=null)GUI.DrawTexture(new Rect(3,row.y+3,34,34),c.icon,ScaleMode.ScaleToFit,true);
     float assign=Math.Min(155f,row.width*.4f);
     Widgets.Label(new Rect(43,row.y+3,Math.Max(10,row.width-48-assign),34),c.Label);
     var owner=layout.Owner(pair.Key);
     if(Widgets.ButtonText(new Rect(row.xMax-assign,row.y+5,assign-3,30),owner?.name??"GF_None".Translate().ToString())){
      string k=pair.Key;var opts=new List<FloatMenuOption>{new FloatMenuOption("GF_None".Translate(),()=>layout.Move(k,null))};
      foreach(var f in layout.folders){var target=f;opts.Add(new FloatMenuOption(target.name,()=>layout.Move(k,target)));}
      Find.WindowStack.Add(new FloatMenu(opts));
     }
    }
   }finally{Widgets.EndScrollView();}
  }
 }
 public sealed class FolderWindow : Window {
  readonly Pawn pawn;readonly Folder folder;Vector2 scroll;
  public FolderWindow(Pawn p,Folder f){pawn=p;folder=f;doCloseX=true;closeOnAccept=false;closeOnClickedOutside=true;absorbInputAroundWindow=false;}
  public override Vector2 InitialSize=>new Vector2(Math.Min(540f,UI.screenWidth-40f),Math.Min(380f,UI.screenHeight-50f));
  protected override void SetInitialSizeAndPosition(){base.SetInitialSizeAndPosition();windowRect.y=Math.Max(0,UI.screenHeight-windowRect.height-145f);}
  public override void DoWindowContents(Rect r){
   if(!Hooks.Selected(pawn)){Close(false);return;}
   Text.Font=GameFont.Medium;Widgets.Label(new Rect(0,0,r.width-125,34),folder.name);Text.Font=GameFont.Small;
   if(Widgets.ButtonText(new Rect(r.width-115,0,90,30),"GF_Manage".Translate())){Close(false);Hooks.Manage(pawn);return;}
   var available=Hooks.Available(Hooks.Raw(pawn));var commands=folder.keys.Distinct().Where(available.ContainsKey).Select(k=>available[k]).ToList();
   if(commands.Count==0){Widgets.Label(new Rect(0,44,r.width,50),"GF_Empty".Translate());return;}
   var viewport=new Rect(0,42,r.width,Math.Max(50,r.height-42));int cols=Math.Max(1,Mathf.FloorToInt((viewport.width-16)/85f));
   var inner=new Rect(0,0,viewport.width-16,Math.Max(viewport.height-1,Mathf.CeilToInt(commands.Count/(float)cols)*90f));
   Command clicked=null;Event interaction=null;List<FloatMenuOption> menu=null;bool right=false;
   Widgets.BeginScrollView(viewport,ref scroll,inner);
   try{
    for(int i=0;i<commands.Count;i++){
     var c=commands[i];var pos=new Vector2((i%cols)*85f,(i/cols)*90f);
     // Keep the original command UI, cooldown and disabled reason.
     var key=c.hotKey;c.hotKey=null;
     GizmoResult res;try{res=c.GizmoOnGUI(pos,75f,default(GizmoRenderParms));}finally{c.hotKey=key;}
     if(res.State==GizmoState.Mouseover)c.GizmoUpdateOnMouseover();
     if(clicked!=null||menu!=null||c.Disabled)continue;
     if(res.State==GizmoState.Interacted){clicked=c;interaction=res.InteractEvent??Event.current;}
     else if(res.State==GizmoState.OpenedFloatMenu){var opts=c.RightClickFloatMenuOptions.ToList();if(opts.Count>0)menu=opts;else{clicked=c;interaction=res.InteractEvent??Event.current;right=true;}}
    }
   }finally{Widgets.EndScrollView();}
   if(menu!=null){Find.WindowStack.Add(new FloatMenu(menu));Event.current.Use();}
   else if(clicked!=null){
    if(!right)Close(false);
    clicked.ProcessInput(interaction);clicked.ProcessGroupInput(interaction,new List<Gizmo>{clicked});
    Event.current.Use();
   }
  }
 }
}
