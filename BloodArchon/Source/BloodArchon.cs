using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Verse.AI;
using VEF.Abilities;
using Ability = VEF.Abilities.Ability;
using AbilityDef = VEF.Abilities.AbilityDef;

namespace BloodArchon {
 public class Settings : ModSettings {
  public float capacity=100, perLevel=10, compensation=.75f, cost=1, damage=1;
  public bool useBody=true, safety=true;
  public override void ExposeData(){ Scribe_Values.Look(ref capacity,"capacity",100); Scribe_Values.Look(ref perLevel,"perLevel",10); Scribe_Values.Look(ref compensation,"compensation",.75f); Scribe_Values.Look(ref cost,"cost",1); Scribe_Values.Look(ref damage,"damage",1); Scribe_Values.Look(ref useBody,"useBody",true); Scribe_Values.Look(ref safety,"safety",true); }
 }
 public class BloodMod : Mod {
  public static Settings Config=new Settings();
  public BloodMod(ModContentPack content):base(content){Config=GetSettings<Settings>();}
  public override string SettingsCategory()=>"BA_Title".Translate();
  public override void DoSettingsWindowContents(Rect rect){ var l=new Listing_Standard(); l.Begin(rect); l.Label("BA_Capacity".Translate()+": "+Config.capacity.ToString("F0")); Config.capacity=l.Slider(Config.capacity,50,300); l.Label("BA_PerLevel".Translate()+": "+Config.perLevel.ToString("F0")); Config.perLevel=l.Slider(Config.perLevel,0,30); l.Label("BA_Compensation".Translate()+": "+Config.compensation.ToStringPercent()); Config.compensation=l.Slider(Config.compensation,0,1); l.Label("BA_Cost".Translate()+": "+Config.cost.ToString("F2")); Config.cost=l.Slider(Config.cost,.25f,3); l.Label("BA_Damage".Translate()+": "+Config.damage.ToString("F2")); Config.damage=l.Slider(Config.damage,.25f,3); l.CheckboxLabeled("BA_UseBody".Translate(),ref Config.useBody); l.CheckboxLabeled("BA_Safety".Translate(),ref Config.safety); l.End(); }
 }
 public class Archon : IExposable {
  public Pawn pawn; public float blood=100; public bool fortify, shield, aura, emergency; public bool wasUnconscious; public int nextTend, nextAura, nextUpkeep, nextHeal;
  public void ExposeData(){ Scribe_References.Look(ref pawn,"pawn"); Scribe_Values.Look(ref blood,"blood",100); Scribe_Values.Look(ref fortify,"fortify"); Scribe_Values.Look(ref shield,"shield"); Scribe_Values.Look(ref aura,"aura"); Scribe_Values.Look(ref emergency,"emergency"); Scribe_Values.Look(ref wasUnconscious,"wasUnconscious"); Scribe_Values.Look(ref nextTend,"nextTend"); Scribe_Values.Look(ref nextAura,"nextAura"); Scribe_Values.Look(ref nextUpkeep,"nextUpkeep"); Scribe_Values.Look(ref nextHeal,"nextHeal"); }
  public float Max=>Math.Max(1,BloodMod.Config.capacity+Math.Max(0,Rules.Level(pawn)-1)*BloodMod.Config.perLevel);
  public float Available(bool poolOnly=false){ float b=Math.Max(0,blood); if(!poolOnly && BloodMod.Config.useBody) b+=Math.Max(0,((BloodMod.Config.safety ? .6f : 1f)-Rules.Loss(pawn))*100); return b; }
  public bool Pay(float units,bool poolOnly=false){ units*=BloodMod.Config.cost; if(Available(poolOnly)+.0001f<units) return false; Spend(units,poolOnly); return true; }
  public float Spend(float units,bool poolOnly=false){ units=Math.Max(0,Math.Min(Available(poolOnly),units)); float fromPool=Math.Min(blood,units); blood-=fromPool; if(units>fromPool) Rules.SetLoss(pawn,Rules.Loss(pawn)+(units-fromPool)/100); return units; }
  public void Receive(float units){ if(units<=0||pawn.Dead)return; float body=Math.Min(units,Rules.Loss(pawn)*100); Rules.SetLoss(pawn,Rules.Loss(pawn)-body/100); blood=Math.Min(Max,blood+units-body); }
  public void RegenerateFullBody(int ticks){ if(blood<Max && Rules.Loss(pawn)<=.00001f && pawn.health.hediffSet.BleedRateTotal<=0f) blood=Math.Min(Max,blood+100f/3f*ticks/60000f); }
  public float RouteNatural(float old,float value){ if(value>old){float use=Math.Min(blood,(value-old)*100*BloodMod.Config.compensation); blood-=use; return value-use/100; } if(value<old && blood<Max){ float recovery=Math.Max(0,old-Math.Max(0,value)); float bodyFirst=Math.Min(recovery,Math.Max(0,old-.05f)); float reserve=Math.Min((recovery-bodyFirst)*100,Max-blood); blood+=reserve; return Math.Max(0,value)+reserve/100; } return value; }
  public bool Has(string suffix)=>Rules.Has(pawn,"BA_"+suffix);
  public void Tick(int now){ if(pawn==null||pawn.Dead)return; blood=Mathf.Clamp(blood,0,Max); bool unconscious=pawn.Downed; if(unconscious&&!wasUnconscious&&Has("Fortify")){if(!fortify){fortify=true;emergency=true;}} wasUnconscious=unconscious;
   if(emergency&&!pawn.Downed){emergency=false;fortify=false;}
   if(fortify){Rules.Status(pawn,"BA_Fortified",true); if(now>=nextTend){nextTend=now+Mathf.RoundToInt(Mathf.Lerp(180,60,Mathf.Clamp01((Rules.Quality(pawn)-.4f)/1.1f))); var wound=Rules.Wounds(pawn).FirstOrDefault(); if(wound!=null&&Pay(Rules.TendCost(wound),emergency||pawn.Downed))Rules.Tend(wound,pawn); }
    if(now>=nextHeal && !Rules.Wounds(pawn).Any(h=>h.BleedRate>0f)){nextHeal=now+600; var injury=pawn.health.hediffSet.hediffs.OfType<Hediff_Injury>().Where(h=>h.Severity>.01f && h.IsTended() && !h.IsPermanent()).OrderByDescending(h=>h.Severity).FirstOrDefault(); if(injury!=null&&Pay(.25f,emergency||pawn.Downed))injury.Heal(.5f); }} else Rules.Status(pawn,"BA_Fortified",false);
   if(aura){if(!pawn.Spawned||pawn.Downed||pawn.Dead){aura=false;return;} if(now>=nextUpkeep){nextUpkeep=now+60;if(!Pay(1)){aura=false;return;}} if(now>=nextAura){nextAura=now+180; foreach(var p in pawn.Map.mapPawns.AllPawnsSpawned.ToList()){ if(p==pawn||!Rules.HasBlood(p)||p.Downed||p.Position.DistanceTo(pawn.Position)>30||!GenSight.LineOfSight(pawn.Position,p.Position,pawn.Map))continue; Receive(Rules.Drain(p,.02f)); if(!p.Dead)Rules.Cut(p,pawn,Rand.RangeInclusive(2,4),.15f); }}} }
 }
 public class Control : IExposable {
  public Pawn target, owner; public Faction original; public int end;
  public void ExposeData(){Scribe_References.Look(ref target,"target");Scribe_References.Look(ref owner,"owner");Scribe_References.Look(ref original,"original");Scribe_Values.Look(ref end,"end");}
  public void Release(){if(target==null||target.Dead)return; if(target.drafter!=null)target.drafter.Drafted=false; target.jobs?.StopAll(); target.SetFaction(original); Rules.Status(target,"BA_Controlled",false);}
 }
 public class Bat : IExposable {
  public Pawn pawn, owner; public int hits,end,nextJob; public bool split;
  public void ExposeData(){Scribe_References.Look(ref pawn,"pawn");Scribe_References.Look(ref owner,"owner");Scribe_Values.Look(ref hits,"hits");Scribe_Values.Look(ref end,"end");Scribe_Values.Look(ref nextJob,"nextJob");Scribe_Values.Look(ref split,"split");}
  public void Burst(Map map,IntVec3 pos){if(map==null)return;var a=owner!=null&&owner.Spawned&&owner.Map==map&&owner.Position.DistanceTo(pos)<=2.5f?World.Get?.Find(owner):null; var foe=map.mapPawns.AllPawnsSpawned.Where(p=>!p.Dead&&owner!=null&&p.HostileTo(owner)&&p.Position.DistanceTo(pos)<=2.5f).OrderBy(p=>p.Position.DistanceTo(pos)).FirstOrDefault(); if(a!=null&&(foe==null||owner.Position.DistanceTo(pos)<=foe.Position.DistanceTo(pos)))a.blood=Math.Min(a.Max,a.blood+6);else if(foe!=null)for(int i=0;i<3&&!foe.Dead;i++)Rules.Cut(foe,owner,3,.3f); var fx=DefDatabase<FleckDef>.GetNamedSilentFail("PsycastAreaEffect");if(fx!=null)FleckMaker.Static(pos,map,fx,2.5f);}
  public void Tick(int now){if(pawn==null||pawn.Dead||!pawn.Spawned)return; if(pawn.needs?.food!=null)pawn.needs.food.CurLevelPercentage=1;if(pawn.needs?.rest!=null)pawn.needs.rest.CurLevelPercentage=1; if(!split&&hits>=5&&owner!=null&&!owner.Dead){split=true;World.Get.SpawnBat(owner,pawn.Position,pawn.Map,end,true);} if(now<nextJob)return; nextJob=now+90; if(pawn.Downed)return; var target=pawn.Map.mapPawns.AllPawnsSpawned.Where(p=>!p.Dead&&!p.Downed&&owner!=null&&p.HostileTo(owner)&&pawn.CanReach(p,PathEndMode.Touch,Danger.Deadly)).OrderBy(p=>p.Position.DistanceToSquared(pawn.Position)).FirstOrDefault(); if(target!=null&&(pawn.CurJobDef!=JobDefOf.AttackMelee||pawn.CurJob.targetA.Thing!=target))pawn.jobs.StartJob(JobMaker.MakeJob(JobDefOf.AttackMelee,target),JobCondition.InterruptForced); }
 }
 public class World : GameComponent {
  public List<Archon> archons=new List<Archon>(); public List<Control> controls=new List<Control>(); public List<Bat> bats=new List<Bat>();
  public static World Get=>Current.Game?.GetComponent<World>();
  public World(Game game){}
  public override void ExposeData(){Scribe_Collections.Look(ref archons,"archons",LookMode.Deep);Scribe_Collections.Look(ref controls,"controls",LookMode.Deep);Scribe_Collections.Look(ref bats,"bats",LookMode.Deep);if(Scribe.mode==LoadSaveMode.PostLoadInit){if(archons==null)archons=new List<Archon>();if(controls==null)controls=new List<Control>();if(bats==null)bats=new List<Bat>();}}
  public Archon Find(Pawn p)=>archons.FirstOrDefault(x=>x.pawn==p);
  public Archon Ensure(Pawn p){if(p==null||p.Dead||p.Faction!=Faction.OfPlayer||!Rules.HasBlood(p))return null;var a=Find(p);if(a==null){a=new Archon{pawn=p};a.blood=a.Max;archons.Add(a);}return a;}
  public void SpawnBat(Pawn owner,IntVec3 pos,Map map,int end,bool split=false){if(owner==null||map==null)return;var kind=DefDatabase<PawnKindDef>.GetNamed("BA_BloodBat");var p=PawnGenerator.GeneratePawn(kind,owner.Faction);p.ageTracker.AgeBiologicalTicks=3600000;var cell=GenRadial.RadialCellsAround(pos,2,true).Where(c=>c.InBounds(map)&&c.Standable(map)).DefaultIfEmpty(pos).RandomElement();if(!cell.IsValid||!cell.InBounds(map)||!cell.Standable(map))cell=pos;GenSpawn.Spawn(p,cell,map);bats.Add(new Bat{pawn=p,owner=owner,end=end,split=split});}
  public override void GameComponentTick(){int n=Verse.Find.TickManager.TicksGame;if(n%120==0){foreach(var map in Verse.Find.Maps)foreach(var p in map.mapPawns.AllPawnsSpawned)if(p.Faction==Faction.OfPlayer&&Rules.IsArchon(p))Ensure(p);archons.RemoveAll(a=>a.pawn==null||a.pawn.Destroyed||a.pawn.Dead);}
   if(n%60==0){foreach(var a in archons.ToList())if(a.pawn!=null&&!a.pawn.Dead)a.RegenerateFullBody(60);}
   if(n%15!=0)return;foreach(var a in archons.ToList())a.Tick(n);
   foreach(var c in controls.ToList())if(c.target==null||c.target.Dead||c.owner==null||c.owner.Dead||c.owner.Downed||n>=c.end){c.Release();controls.Remove(c);}
   foreach(var b in bats.ToList()){if(b.pawn==null||b.pawn.Dead||b.pawn.Destroyed){bats.Remove(b);continue;}if(b.owner==null||b.owner.Dead||n>=b.end){bats.Remove(b);b.pawn.Destroy(DestroyMode.Vanish);continue;}b.Tick(n);}
  }
 }
 public static class Rules {
  [ThreadStatic] public static Pawn naturalPawn; [ThreadStatic] public static bool explicitLoss;
  public static HediffDef BloodDef=>HediffDefOf.BloodLoss;
  public static float Loss(Pawn p)=>p?.health?.hediffSet.GetFirstHediffOfDef(BloodDef)?.Severity??0f;
  public static bool HasBlood(Pawn p)=>p!=null&&!p.Dead&&p.health!=null&&p.RaceProps.IsFlesh&&p.def.defName!="BA_BloodBat";
  public static bool Has(Pawn p,string name)=>p?.GetComp<CompAbilities>()?.LearnedAbilities.Any(a=>a.def.defName==name)==true;
  public static bool IsArchon(Pawn p){if(p?.GetComp<CompAbilities>()?.LearnedAbilities.Any(a=>a.def.defName.StartsWith("BA_"))==true)return true;var implant=p?.health?.hediffSet.hediffs.FirstOrDefault(h=>h.def.defName=="VPE_PsycastAbilityImplant");if(implant==null)return false;var f=AccessTools.Field(implant.GetType(),"unlockedPaths");var paths=(f?.GetValue(implant)??AccessTools.Property(implant.GetType(),"UnlockedPaths")?.GetValue(implant,null)) as System.Collections.IEnumerable;return paths!=null&&paths.Cast<object>().OfType<Def>().Any(d=>d.defName=="BA_BloodArchon");}
  public static int Level(Pawn p){var h=p?.health?.hediffSet.hediffs.FirstOrDefault(x=>x.def.defName=="VPE_PsycastAbilityImplant");if(h==null)return 1;var f=AccessTools.Field(h.GetType(),"level");var pr=AccessTools.Property(h.GetType(),"Level");object v=f?.GetValue(h)??pr?.GetValue(h,null);return v is int i?Math.Max(1,i):1;}
  public static void SetLoss(Pawn p,float loss){if(p==null||p.Dead)return;bool prev=explicitLoss;explicitLoss=true;try{var h=p.health.hediffSet.GetFirstHediffOfDef(BloodDef);if(loss<=0){if(h!=null)p.health.RemoveHediff(h);return;}if(h==null){h=HediffMaker.MakeHediff(BloodDef,p);h.Severity=Mathf.Clamp01(loss);p.health.AddHediff(h);}else h.Severity=Mathf.Clamp01(loss); }finally{explicitLoss=prev;}}
  public static float Drain(Pawn p,float amount){if(!HasBlood(p))return 0;float old=Loss(p);float actual=Math.Min(1-old,amount);SetLoss(p,old+actual);return actual*100*Mathf.Clamp(p.BodySize,.05f,10f);}
  public static float Sens(Pawn p)=>Mathf.Clamp(p.GetStatValue(StatDefOf.PsychicSensitivity),.1f,5f);
  public static float Quality(Pawn p)=>Mathf.Clamp(.15f+.6f*Sens(p)+.025f*(p.skills?.GetSkill(SkillDefOf.Medicine).Level??0)+.02f*(Level(p)-1),.1f,2f);
  public static IEnumerable<Hediff_Injury> Wounds(Pawn p)=>p.health.hediffSet.hediffs.OfType<Hediff_Injury>().Where(h=>h.TendableNow()).OrderByDescending(h=>h.BleedRate).ThenByDescending(h=>h.Severity).ToList();
  public static float TendCost(Hediff_Injury h)=>Mathf.Clamp(h.Severity/5f,1,5);
  public static void Tend(Hediff_Injury h,Pawn caster)=>h.Tended(Quality(caster),2f,1);
  public static void Status(Pawn p,string def,bool active){var d=DefDatabase<HediffDef>.GetNamed(def);var h=p.health.hediffSet.GetFirstHediffOfDef(d);if(active&&h==null)p.health.AddHediff(d);else if(!active&&h!=null)p.health.RemoveHediff(h);}
  public static void Cut(Pawn p,Pawn caster,float amount,float ap){p.TakeDamage(new DamageInfo(DamageDefOf.Cut,amount*BloodMod.Config.damage,ap,-1,caster));}
  public static void Pulse(Pawn p,float size=1f){ if(p==null||!p.Spawned)return;var fx=DefDatabase<FleckDef>.GetNamedSilentFail("PsycastPsychicEffect");if(fx!=null)FleckMaker.Static(p.Position,p.Map,fx,size); }
  public static void Reject()=>Messages.Message("BA_NotEnough".Translate(),MessageTypeDefOf.RejectInput,false);
 }
 public class BloodAbility : Ability {
  private Pawn BloodCaster => base.Caster as Pawn;
  public override bool ValidateTarget(LocalTargetInfo target,bool showMessages=true){if(BloodCaster==null||!base.ValidateTarget(target,showMessages))return false;var p=target.Thing as Pawn;switch(def.defName){case "BA_Drain":return p!=BloodCaster&&Rules.HasBlood(p);case "BA_Control":return p!=BloodCaster&&Rules.HasBlood(p)&&p.RaceProps.Humanlike&&p.HostileTo(BloodCaster)&&p.Faction!=null&&!p.IsPrisoner;case "BA_Transfuse":return Rules.HasBlood(p)&&p.Faction==BloodCaster.Faction;case "BA_Spear":return p!=null&&p!=BloodCaster&&!p.Dead;default:return true;}}
  public override void Cast(params GlobalTargetInfo[] targets){var a=World.Get?.Ensure(BloodCaster);if(a==null)return;string id=def.defName;Pawn p=targets.Length>0?targets[0].Thing as Pawn:null;Thing t=targets.Length>0?targets[0].Thing:null;int n=Find.TickManager.TicksGame;float price=0;int count=0;List<Hediff_Injury> wounds=null;float transfer=0;
   switch(id){case "BA_Drain":if(!Rules.HasBlood(p)||p==BloodCaster)return;break;case "BA_Spear":if(t==null||t.Destroyed)return;price=12;break;case "BA_Control":if(!Rules.HasBlood(p)||!p.HostileTo(BloodCaster)||!p.RaceProps.Humanlike||p.IsPrisoner||World.Get.controls.Count(c=>c.owner==BloodCaster)>=2)return;price=25;break;case "BA_Transfuse":if(!Rules.HasBlood(p)||p.Faction!=BloodCaster.Faction)return;wounds=Rules.Wounds(p).ToList();transfer=Math.Min(30,Rules.Loss(p)*100);price=transfer+wounds.Sum(Rules.TendCost);break;case "BA_Bats":count=Rand.RangeInclusive(3,5);price=count*8;break;case "BA_Aura":if(!a.aura)price=20;break;}
   if(price>0&&!a.Pay(price)){Rules.Reject();return;}base.Cast(targets);if(BloodCaster.Dead)return;Rules.Pulse(p??BloodCaster);
   switch(id){case "BA_Fortify":a.fortify=!a.fortify;a.emergency=false;break;case "BA_Shield":a.shield=!a.shield;break;case "BA_Drain":bool consent=p.Downed||(p.Faction==BloodCaster.Faction&&!p.Drafted&&!p.IsPrisoner);a.Receive(Rules.Drain(p,consent ? .4f : .1f));break;case "BA_Spear":t.TakeDamage(new DamageInfo(DamageDefOf.Stab,45*Rules.Sens(BloodCaster)*BloodMod.Config.damage,.8f,-1,BloodCaster));if(p!=null&&!p.Dead&&Rules.HasBlood(p))Rules.Cut(p,BloodCaster,5*Rules.Sens(BloodCaster),.8f);break;case "BA_Control":var c=new Control{target=p,owner=BloodCaster,original=p.Faction,end=n+Mathf.RoundToInt(5400*Rules.Sens(BloodCaster)/Math.Max(.5f,Rules.Sens(p)))};p.jobs?.StopAll();p.SetFaction(BloodCaster.Faction);Rules.Status(p,"BA_Controlled",true);World.Get.controls.Add(c);break;case "BA_Transfuse":foreach(var w in wounds)if(p.health.hediffSet.hediffs.Contains(w))Rules.Tend(w,BloodCaster);Rules.SetLoss(p,Rules.Loss(p)-transfer/100);break;case "BA_Bats":for(int i=0;i<count;i++)World.Get.SpawnBat(BloodCaster,BloodCaster.Position,BloodCaster.Map,n+10800);break;case "BA_Aura":a.aura=!a.aura;a.nextAura=n;a.nextUpkeep=n;break;}
  }
 }
 [StaticConstructorOnStartup] public static class Hooks {
  static Hooks(){var h=new Harmony("domenklay.bloodarchon");Try(h,AccessTools.PropertySetter(typeof(Hediff),"Severity"),nameof(Severity),null); var methods=typeof(Pawn_HealthTracker).GetMethods(AccessTools.all).Where(m=>m.Name=="HealthTickInterval"||m.Name=="HealthTick").ToList();var tick=methods.FirstOrDefault(m=>m.Name=="HealthTickInterval")??methods.FirstOrDefault();Try(h,tick,nameof(HealthStart),nameof(HealthEnd),nameof(HealthFinal));Try(h,AccessTools.Method(typeof(Thing),"TakeDamage",new[]{typeof(DamageInfo)}),nameof(DamageStart),nameof(DamageEnd));Try(h,typeof(Pawn).GetMethods(AccessTools.all).FirstOrDefault(m=>m.Name=="Kill"&&m.GetParameters().Length>=1),nameof(KillStart),nameof(KillEnd));Try(h,AccessTools.Method(typeof(Pawn),"GetGizmos"),null,nameof(Gizmos));}
  static void Try(Harmony h,MethodBase method,string pre,string post,string final=null){if(method==null){Log.Warning("[BloodArchon] Missing hook: "+pre+"/"+post);return;}try{h.Patch(method,pre==null?null:new HarmonyMethod(typeof(Hooks),pre),post==null?null:new HarmonyMethod(typeof(Hooks),post),null,final==null?null:new HarmonyMethod(typeof(Hooks),final));}catch(Exception e){Log.Error("[BloodArchon] Hook failed: "+method+" "+e);}}
  public static void HealthStart(Pawn ___pawn,out Pawn __state){__state=Rules.naturalPawn;Rules.naturalPawn=___pawn;}
  public static void HealthEnd(Pawn __state){Rules.naturalPawn=__state;}
  public static Exception HealthFinal(Exception __exception,Pawn __state){Rules.naturalPawn=__state;return __exception;}
  public static void Severity(Hediff __instance,ref float value){if(Rules.explicitLoss||Rules.naturalPawn==null||__instance.def!=Rules.BloodDef||__instance.pawn!=Rules.naturalPawn)return;var a=World.Get?.Find(__instance.pawn);if(a!=null)value=a.RouteNatural(__instance.Severity,value);}
  public static bool DamageStart(Thing __instance,ref DamageInfo dinfo,ref DamageWorker.DamageResult __result){if(!(__instance is Pawn p))return true;var a=World.Get?.Find(p);if(a==null||!a.shield||p.Dead||dinfo.Amount<=0)return true;var name=dinfo.Def.defName;if(name=="Flame"||name=="Burn"||name=="Frostbite"||name=="ToxicBuildup")return true;float ratio=2*Rules.Sens(p)/BloodMod.Config.cost;float absorbed=a.Spend(dinfo.Amount/ratio)*ratio;float rest=Math.Max(0,dinfo.Amount-absorbed);if(rest<.001f){__result=new DamageWorker.DamageResult();return false;}a.shield=false;dinfo.SetAmount(rest);return true;}
  public static void DamageEnd(Thing __instance,DamageInfo dinfo,DamageWorker.DamageResult __result){if(__result==null||__result.totalDamageDealt<=0||!(dinfo.Instigator is Pawn attacker)||!(__instance is Pawn p))return;var b=World.Get?.bats.FirstOrDefault(x=>x.pawn==attacker);if(b!=null&&b.owner!=null&&p.HostileTo(b.owner)){if(!b.split)b.hits++;if(!p.Dead&&Rules.HasBlood(p)&&dinfo.Def!=DamageDefOf.Cut)Rules.Cut(p,b.owner,1.5f,.6f);}}
  public class DeathState {public Bat b;public Map map;public IntVec3 pos;}
  public static void KillStart(Pawn __instance,out DeathState __state){var b=World.Get?.bats.FirstOrDefault(x=>x.pawn==__instance);__state=b==null?null:new DeathState{b=b,map=__instance.MapHeld,pos=__instance.PositionHeld};}
  public static void KillEnd(Pawn __instance,DeathState __state){if(__state==null||!__instance.Dead)return;World.Get?.bats.Remove(__state.b);__state.b.Burst(__state.map,__state.pos);}
  public static IEnumerable<Gizmo> Gizmos(IEnumerable<Gizmo> __result,Pawn __instance){foreach(var g in __result)yield return g;if(__instance.Faction!=Faction.OfPlayer||!Rules.IsArchon(__instance))yield break;var a=World.Get?.Ensure(__instance);if(a!=null){yield return new BloodGizmo{a=a};var pack=__instance.inventory?.innerContainer.FirstOrDefault(t=>t.def.defName=="HemogenPack");if(pack!=null)yield return new Command_Action{defaultLabel="BA_Pack".Translate(),defaultDesc="BA_PackDesc".Translate(),icon=ContentFinder<Texture2D>.Get("BloodArchon/Abilities/Reserve"),action=()=>{if(pack.Destroyed||!__instance.inventory.innerContainer.Contains(pack))return;if(a.blood>=a.Max&&Rules.Loss(__instance)<=0)return;pack.SplitOff(1).Destroy();a.Receive(20);}};}}
 }
 public class BloodGizmo : Gizmo {
  public Archon a; public override float GetWidth(float maxWidth)=>Math.Min(190,maxWidth);
  public override GizmoResult GizmoOnGUI(Vector2 topLeft,float maxWidth,GizmoRenderParms parms){var r=new Rect(topLeft.x,topLeft.y,GetWidth(maxWidth),75);Widgets.DrawBoxSolid(r,new Color(.14f,.055f,.075f));Text.Font=GameFont.Tiny;Widgets.Label(new Rect(r.x+6,r.y+3,r.width-12,20),"BA_Pool".Translate()+" "+a.blood.ToString("F0")+" / "+a.Max.ToString("F0"));Widgets.FillableBar(new Rect(r.x+6,r.y+23,r.width-12,12),a.blood/a.Max);Widgets.Label(new Rect(r.x+6,r.y+37,r.width-12,20),"BA_Body".Translate()+" "+(1-Rules.Loss(a.pawn)).ToStringPercent());Widgets.Label(new Rect(r.x+6,r.y+54,r.width-12,18),(a.fortify?"+ ":"– ")+"BA_FortifyShort".Translate()+"  "+(a.shield?"+ ":"– ")+"BA_ShieldShort".Translate()+"  "+(a.aura?"+ ":"– ")+"BA_AuraShort".Translate());Text.Font=GameFont.Small;return new GizmoResult(GizmoState.Clear);}
 }
}
