using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace GizmoFolders {
 public static class L {
  public static string T(string key)=>("GF_"+key).Translate();
 }
 public class Folder : IExposable {
  public string id=Guid.NewGuid().ToString("N"),name="";
  public List<string> keys=new List<string>();
  public void ExposeData(){
   Scribe_Values.Look(ref id,"id");Scribe_Values.Look(ref name,"name","");
   Scribe_Collections.Look(ref keys,"keys",LookMode.Value);
   if(keys==null)keys=new List<string>();
  }
 }
 public class PawnLayout : IExposable {
  public string pawnId;
  public List<Folder> folders=new List<Folder>();
  public void ExposeData(){Scribe_Values.Look(ref pawnId,"pawnId");Scribe_Collections.Look(ref folders,"folders",LookMode.Deep);if(folders==null)folders=new List<Folder>();}
  public Folder Owner(string key)=>folders.FirstOrDefault(f=>f.keys.Contains(key));
  public void Assign(string key,Folder folder){foreach(var f in folders)f.keys.RemoveAll(k=>k==key);if(folder!=null)folder.keys.Add(key);}
 }
 public class Layouts : GameComponent {
  public List<PawnLayout> entries=new List<PawnLayout>();
  public Layouts(Game game){}
  public override void ExposeData(){Scribe_Collections.Look(ref entries,"gizmoFolderLayouts",LookMode.Deep);if(entries==null)entries=new List<PawnLayout>();}
  public PawnLayout Get(Pawn p){
   string id=p.GetUniqueLoadID();var entry=entries.FirstOrDefault(e=>e.pawnId==id);
   if(entry==null){entry=new PawnLayout{pawnId=id};entries.Add(entry);}return entry;
  }
 }
 public class Entry {
  public string key,label; public Command command;
 }
 public static class Identity {
  static readonly Dictionary<Type,MemberInfo> abilityMembers=new Dictionary<Type,MemberInfo>();
  static readonly Dictionary<Type,MemberInfo> defMembers=new Dictionary<Type,MemberInfo>();
  static MemberInfo FindMember(Type t,string[] names){
   for(Type current=t;current!=null;current=current.BaseType){
    const BindingFlags flags=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.DeclaredOnly;
    foreach(string name in names){
     var field=current.GetField(name,flags);if(field!=null)return field;
     var prop=current.GetProperty(name,flags);if(prop!=null&&prop.GetIndexParameters().Length==0)return prop;
    }
   }return null;
  }
  static object Read(MemberInfo m,object target){if(m is FieldInfo f)return f.GetValue(target);if(m is PropertyInfo p)return p.GetValue(target,null);return null;}
  public static string Key(Command c){
   string type=c.GetType().FullName;
   try{
    if(c is Command_Ability vanilla&&vanilla.Ability?.def!=null)return "ability:"+vanilla.Ability.def.defName;
    Type t=c.GetType();if(!abilityMembers.TryGetValue(t,out var m)){m=FindMember(t,new[]{"ability","Ability","psycast","Psycast"});abilityMembers[t]=m;}
    object a=Read(m,c);if(a!=null){
     if(!defMembers.TryGetValue(a.GetType(),out var dm)){dm=FindMember(a.GetType(),new[]{"def","Def"});defMembers[a.GetType()]=dm;}
     if(Read(dm,a) is Def d)return type+":"+d.defName;
    }
   }catch{}
   string label=c.defaultLabel;if(string.IsNullOrEmpty(label))label=c.Label;
   return type+"|"+(label??"")+"|"+(c.hotKey?.defName??"")+"|"+(c.icon?.name??"");
  }
 }
 public class FolderCommand : Command_Action {
  public int count;
  public Action manage;
  public List<Command> preview;
  public Rect anchor;
  public override GizmoResult GizmoOnGUI(Vector2 topLeft,float maxWidth,GizmoRenderParms parms){
   anchor=new Rect(topLeft.x,topLeft.y,GetWidth(maxWidth),75f);
   return base.GizmoOnGUI(topLeft,maxWidth,parms);
  }
  public override void DrawIcon(Rect rect,Material buttonMat,GizmoRenderParms parms){
   if(preview==null||preview.Count==0){base.DrawIcon(rect,buttonMat,parms);return;}
   var old=GUI.color;
   try{
    int n=Math.Min(6,preview.Count);int cols=n<=4?2:3;
    int rows=(n+cols-1)/cols;
    float cell=Math.Min(24f,(rect.width-8f)/cols);
    float startX=rect.x+5f,startY=rect.y+4f;
    for(int i=0;i<n;i++){
     var c=preview[i];var tile=new Rect(startX+(i%cols)*cell,startY+(i/cols)*cell,cell-2f,cell-2f);
     GUI.color=Color.white;Widgets.DrawBoxSolid(tile,new Color(.07f,.09f,.11f,.9f));
     if(c.icon!=null){GUI.color=c.Disabled?new Color(.55f,.55f,.55f,1f):Color.white;GUI.DrawTexture(tile.ContractedBy(1f),c.icon,ScaleMode.ScaleToFit,true);}
    }
   }finally{GUI.color=old;}
  }
  public override string TopRightLabel=>count>0?count.ToString():null;
  public override IEnumerable<FloatMenuOption> RightClickFloatMenuOptions{
   get{if(manage!=null)yield return new FloatMenuOption(L.T("Manage"),manage);}
  }
 }
 [StaticConstructorOnStartup]
 public static class Hooks {
  static Pawn scopePawn,lastPawn;
  static List<Entry> latestEntries=new List<Entry>();
  public static List<Entry> Entries(Pawn p)=>lastPawn==p?latestEntries:new List<Entry>();
  static Texture2D folderIcon;
  static Hooks(){
   var h=new Harmony("domenklay.gizmofolders");
   var outer=AccessTools.Method(typeof(GizmoGridDrawer),"DrawGizmoGridFor");
   var inner=AccessTools.Method(typeof(GizmoGridDrawer),"DrawGizmoGrid");
   if(outer==null||inner==null){Log.Warning("[GizmoFolders] Gizmo grid methods not found; mod inactive.");return;}
   h.Patch(outer,prefix:new HarmonyMethod(typeof(Hooks),nameof(Begin)),finalizer:new HarmonyMethod(typeof(Hooks),nameof(End)));
   h.Patch(inner,prefix:new HarmonyMethod(typeof(Hooks),nameof(Filter)){priority=Priority.Last});
  }
  public static void Begin(IEnumerable<object> __0,out Pawn __state){
   __state=scopePawn;scopePawn=null;
   if(Current.Game==null||__0==null)return;
   var selected=__0.Take(2).ToList();
   if(selected.Count==1&&selected[0] is Pawn p&&p.Faction==Faction.OfPlayer&&!p.Dead)scopePawn=p;
   if(scopePawn!=lastPawn){lastPawn=scopePawn;latestEntries=new List<Entry>();}
  }
  public static Exception End(Exception __exception,Pawn __state){scopePawn=__state;return __exception;}
  static Texture2D FolderTexture(){
   if(folderIcon!=null)return folderIcon;
   folderIcon=new Texture2D(64,64,TextureFormat.RGBA32,false);folderIcon.name="GizmoFoldersFolder";
   var pixels=new Color[64*64];
   for(int y=0;y<64;y++)for(int x=0;x<64;x++){
    bool tab=x>=8&&x<29&&y>=42&&y<51;
    bool rect=x>=5&&x<59&&y>=12&&y<45;
    if(!tab&&!rect)continue;
    bool edge=x==5||x==58||y==12||y==44||(tab&&y==50);
    pixels[y*64+x]=edge?new Color(.66f,.43f,.13f):new Color(.92f,.70f,.30f);
   }
   folderIcon.SetPixels(pixels);folderIcon.Apply();return folderIcon;
  }
  static void Manage(PawnLayout layout,List<Entry> entries){Find.WindowStack.Add(new Organizer(layout,entries));}
  public static void Filter(ref IEnumerable<Gizmo> __0){
   if(scopePawn==null||__0==null||Event.current==null||Event.current.type==EventType.Layout)return;
   try{
    var original=__0.ToList();__0=original;
    var store=Current.Game.GetComponent<Layouts>();if(store==null)return;
    var layout=store.Get(scopePawn);
    var entries=new List<Entry>();var keyByCommand=new Dictionary<Command,string>();
    foreach(var g in original){
     if(!(g is Command c)||c is FolderCommand||!c.Visible)continue;
     if(string.IsNullOrWhiteSpace(c.Label))continue;
     string key=Identity.Key(c);keyByCommand[c]=key;
     if(!entries.Any(e=>e.key==key))entries.Add(new Entry{key=key,label=c.Label,command=c});
    }
    latestEntries=entries;
    if(entries.Count==0)return;
    var folders=layout.folders.Where(f=>entries.Any(e=>f.keys.Contains(e.key))).ToList();

    var inserted=new HashSet<string>();var result=new List<Gizmo>();
    Action manage=()=>Manage(layout,entries);
    foreach(var g in original){
     if(!(g is Command c)||!keyByCommand.TryGetValue(c,out var key)){result.Add(g);continue;}
     var f=layout.Owner(key);
     if(f==null){result.Add(g);continue;}
     if(inserted.Add(f.id)){
      var folder=f;int count=entries.Count(e=>folder.keys.Contains(e.key));
      var order=original.OfType<Command>().Where(cmd=>keyByCommand.TryGetValue(cmd,out var k)&&folder.keys.Contains(k)).Select(cmd=>cmd.Order).DefaultIfEmpty(c.Order).Min();
      var p=scopePawn;
      var button=new FolderCommand{defaultLabel=folder.name,defaultDesc=L.T("OpenTip")+"\n"+L.T("ManageTip"),icon=FolderTexture(),count=count,groupable=false,Order=order,manage=manage,preview=entries.Where(e=>folder.keys.Contains(e.key)).Select(e=>e.command).ToList()};
      button.action=()=>{if(p!=null&&Find.Selector.SingleSelectedThing==p)Find.WindowStack.Add(new FolderWindow(p,layout,folder,button.anchor,count));};
      result.Add(button);
     }

    }
    if(folders.Count==0)result.Add(new FolderCommand{defaultLabel=L.T("Folders"),defaultDesc=L.T("ManageTip"),icon=FolderTexture(),groupable=false,Order=10000f,action=manage,manage=manage});
    __0=result;
   }catch(Exception e){Log.WarningOnce("[GizmoFolders] UI fallback: "+e,927183);}
  }
 }
 public class Organizer : Window {
  readonly PawnLayout layout;readonly List<Entry> entries;
  string newName="",rename="",search="";Folder selected;
  Vector2 scroll;
  public override Vector2 InitialSize=>new Vector2(Math.Min(690f,UI.screenWidth-30f),Math.Min(640f,UI.screenHeight-30f));
  public Organizer(PawnLayout layout,List<Entry> entries){this.layout=layout;this.entries=entries;doCloseX=true;doCloseButton=true;forcePause=true;absorbInputAroundWindow=true;closeOnAccept=false;}
  void SelectFolder(){var options=layout.folders.Select(f=>new FloatMenuOption(f.name,()=>{selected=f;rename=f.name;})).ToList();if(options.Count>0)Find.WindowStack.Add(new FloatMenu(options));}
  void Assignment(Entry e){
   var options=new List<FloatMenuOption>{new FloatMenuOption(L.T("Unfiled"),()=>layout.Assign(e.key,null))};
   foreach(var f in layout.folders){var target=f;options.Add(new FloatMenuOption(f.name,()=>layout.Assign(e.key,target)));}
   Find.WindowStack.Add(new FloatMenu(options));
  }
  public override void DoWindowContents(Rect r){
   Text.Font=GameFont.Medium;Widgets.Label(new Rect(0,0,r.width,32),L.T("Manage"));
   Text.Font=GameFont.Small;Widgets.Label(new Rect(0,34,r.width,40),L.T("Instructions"));
   newName=Widgets.TextField(new Rect(0,78,r.width-128,30),newName);
   if(Widgets.ButtonText(new Rect(r.width-120,78,120,30),L.T("Create"))&&!string.IsNullOrWhiteSpace(newName)){
    string name=newName.Trim();if(name.Length>40)name=name.Substring(0,40);
    if(!layout.folders.Any(f=>f.name==name)){selected=new Folder{name=name};layout.folders.Add(selected);rename=name;newName="";}
   }
   if(Widgets.ButtonText(new Rect(0,114,r.width*.42f,30),selected?.name??L.T("Choose")))SelectFolder();
   float left=r.width*.42f+6;
   rename=Widgets.TextField(new Rect(left,114,r.width-left-146,30),rename);
   if(Widgets.ButtonText(new Rect(r.width-140,114,66,30),L.T("Rename"))&&selected!=null&&!string.IsNullOrWhiteSpace(rename)){
    string name=rename.Trim();if(name.Length>40)name=name.Substring(0,40);if(!layout.folders.Any(f=>f!=selected&&f.name==name)){selected.name=name;rename=name;}
   }
   if(Widgets.ButtonText(new Rect(r.width-70,114,70,30),L.T("Delete"))&&selected!=null){layout.folders.Remove(selected);selected=null;rename="";}
   Widgets.Label(new Rect(0,154,75,28),L.T("Search"));
   search=Widgets.TextField(new Rect(78,154,r.width-78,28),search);
   var shown=entries.Where(e=>string.IsNullOrEmpty(search)||e.label.IndexOf(search,StringComparison.OrdinalIgnoreCase)>=0).ToList();
   float height=Math.Max(50,r.height-242);var outer=new Rect(0,190,r.width,height);var inner=new Rect(0,0,r.width-20,Math.Max(height-1,shown.Count*42f));
   Widgets.BeginScrollView(outer,ref scroll,inner);
   for(int i=0;i<shown.Count;i++){
    var e=shown[i];float y=i*42f;if(i%2==0)Widgets.DrawBoxSolid(new Rect(0,y,inner.width,40),new Color(.18f,.18f,.18f,.7f));
    if(e.command.icon!=null)GUI.DrawTexture(new Rect(4,y+4,32,32),e.command.icon,ScaleMode.ScaleToFit,true);
    Widgets.Label(new Rect(42,y+6,inner.width*.59f-45,32),e.label);
    if(Widgets.ButtonText(new Rect(inner.width*.59f,y+4,inner.width*.41f-4,32),layout.Owner(e.key)?.name??L.T("Unfiled")))Assignment(e);
   }
   Widgets.EndScrollView();
  }
 }
 public class FolderWindow : Window {
  readonly Pawn pawn;readonly PawnLayout layout;readonly Folder folder;readonly Rect anchor;readonly int count;Vector2 scroll;
  const float Cell=80f,Row=86f;
  protected override float Margin=>6f;
  int Columns=>Math.Max(1,Math.Min(Math.Min(5,Math.Max(1,count)),Mathf.FloorToInt((UI.screenWidth-40f)/Cell)));
  int Rows=>Mathf.CeilToInt(Math.Max(1,count)/(float)Columns);
  int ShownRows=>Math.Min(Rows,Math.Max(1,Math.Min(3,Mathf.FloorToInt((UI.screenHeight-90f)/Row))));
  bool ScrollNeeded=>Rows>ShownRows;
  public FolderWindow(Pawn p,PawnLayout l,Folder f,Rect anchor,int count){pawn=p;layout=l;folder=f;this.anchor=anchor;this.count=count;doCloseX=false;closeOnAccept=false;closeOnClickedOutside=true;absorbInputAroundWindow=false;}
  public override Vector2 InitialSize=>new Vector2(Math.Min(UI.screenWidth-4f,Columns*Cell+12f+(ScrollNeeded?18f:0f)),Math.Min(UI.screenHeight-4f,ShownRows*Row+12f));
  protected override void SetInitialSizeAndPosition(){
   var size=InitialSize;
   float x=Mathf.Clamp(anchor.x,2f,Math.Max(2f,UI.screenWidth-size.x-2f));
   float y=anchor.y-size.y-5f;
   if(y<2f&&anchor.yMax+5f+size.y<=UI.screenHeight-2f)y=anchor.yMax+5f;
   y=Mathf.Clamp(y,2f,Math.Max(2f,UI.screenHeight-size.y-2f));
   windowRect=new Rect(x,y,size.x,size.y).Rounded();
  }
  public override void DoWindowContents(Rect r){
   if(pawn==null||pawn.Dead||Find.Selector?.NumSelected!=1||Find.Selector.SingleSelectedThing!=pawn||!layout.folders.Contains(folder)){Close(false);return;}
   var entries=Hooks.Entries(pawn);
   var commands=entries.Where(e=>folder.keys.Contains(e.key)).Select(e=>e.command).Where(c=>c.Visible).ToList();
   if(commands.Count==0){Widgets.Label(r,L.T("Empty"));return;}
   int cols=Columns;
   var viewport=new Rect(0,0,r.width,r.height);
   var inner=new Rect(0,0,cols*Cell,Math.Max(1,Mathf.CeilToInt(commands.Count/(float)cols))*Row);
   Command clicked=null;Event interaction=null;List<FloatMenuOption> menu=null;bool right=false;
   Widgets.BeginScrollView(viewport,ref scroll,inner);
   try{
    for(int i=0;i<commands.Count;i++){
     var c=commands[i];var pos=new Vector2((i%cols)*Cell,(i/cols)*Row);
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
