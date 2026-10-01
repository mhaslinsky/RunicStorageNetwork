#if CRAFT_PREPARATION_RUNTIME_TESTS
#pragma warning disable 0649
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using RunicStorageNetwork;
using RunicStorageNetwork.Logic;
namespace UnityEngine {
 public class Object {public static implicit operator bool(Object o)=>o!=null;public T GetComponent<T>() where T:class=>this is CraftingStation s?s.View as T:null;}
 public static class Time {public static float unscaledTime;}
 public class GameObject:Object {}
}
struct ZDOID {public int Value;public static bool operator ==(ZDOID a,ZDOID b)=>a.Value==b.Value;public static bool operator !=(ZDOID a,ZDOID b)=>!(a==b);public override bool Equals(object o)=>o is ZDOID z&&z==this;public override int GetHashCode()=>Value;public static ZDOID None=>default;}
class ZDO {public ZDOID m_uid=new ZDOID{Value=1};}
class ZNetView {public ZDO z=new ZDO();public ZDO GetZDO()=>z;}
class Skills {public enum SkillType {None,Other}}
class CraftingStation:UnityEngine.Object {public ZNetView View=new ZNetView();public bool m_upgrader;public Skills.SkillType m_craftingSkill;}
class Inventory {public List<Stock> Items=new List<Stock>();public bool ContainsItem(ItemDrop.ItemData item)=>true;public bool CanAddItem(GameObject go,int count)=>true;}
class ItemDrop:UnityEngine.Object {public ItemData m_itemData=new ItemData();public GameObject gameObject=new GameObject();public class ItemData {public int m_quality=1;public Shared m_shared=new Shared();}public class Shared {public string m_dlc="";public int m_maxStackSize=1;}}
class Recipe:UnityEngine.Object {public ItemDrop m_item=new ItemDrop();public bool m_requireOnlyOneIngredient;public string name="wood";public object m_resources;public int Cost=10;public int GetAmount(int quality,out int n,out ItemDrop.ItemData item,int multiplier){n=0;item=null;return multiplier;}}
class Player:UnityEngine.Object {public static Player m_localPlayer;public Inventory inventory=new Inventory();public CraftingStation station=new CraftingStation();public bool IsDead()=>false;public bool NoCostCheat()=>false;public Inventory GetInventory()=>inventory;public CraftingStation GetCurrentCraftingStation()=>station;public float GetSkillFactor(Skills.SkillType skill)=>0;}
class Button {public bool interactable=true;}
class InventoryGui:UnityEngine.Object {
 public static bool Visible=true;public static bool IsVisible()=>Visible;
 public class Pair {public Recipe Recipe {get;set;}public ItemDrop.ItemData ItemData {get;set;}}
 public Pair m_selectedRecipe=new Pair();public Recipe m_craftRecipe;public ItemDrop.ItemData m_craftUpgradeItem;
 public Button m_craftButton=new Button();public int m_multiCraftAmount=5,m_selectedVariant;public bool m_touchMultiCrafting,m_multiCrafting;public float m_craftTimer=-1,m_craftBonusChance;public int m_craftBonusAmount;
 public bool NativeAllowed=true;public int Starts;
 public Action OnUpdate;
 public void UpdateRecipe(Player p,float dt){OnUpdate?.Invoke();CraftPreparation.Selection(this,p);m_craftButton.interactable=NativeAllowed;CraftPreparation.Button(this);}
 public void OnCraftPressed(){if(!CraftPreparation.Press(this))return;Starts++;m_craftRecipe=m_selectedRecipe.Recipe;m_craftUpgradeItem=m_selectedRecipe.ItemData;m_craftTimer=0;CraftPreparation.Pressed(this);}
}
static class ZInput {public static bool GetButton(string key)=>false;}
enum GlobalKeys {NoCraftCost}
class ZoneSystem {public static ZoneSystem instance=new ZoneSystem();public bool GetGlobalKey(GlobalKeys key)=>false;}
class DLCMan {public static DLCMan instance=new DLCMan();public bool IsDLCInstalled(string key)=>true;}
static class Game {public static float m_craftResourceMultiplier=1;}
namespace RunicStorageNetwork {
 static class Plugin {internal static bool Enabled=true;internal static void Debug(string text){}internal static void Info(string text){}internal static void Critical(string key,string message){}}
 static class R {
  internal static T Get<T>(object o,string field)=>(T)o.GetType().GetField(field,BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic).GetValue(o);
  internal static ZNetView View(CraftingStation s)=>s?.View;
  internal static object Call(object o,string method,Type[] args,params object[] values)=>o.GetType().GetMethod(method).Invoke(o,values);
 }
 class Core:UnityEngine.Object {internal ZDOID Id=new ZDOID{Value=3};}
 class Operation {
  internal string Id=Guid.NewGuid().ToString("N"),Target;internal bool Quote;internal int Quality=1,Multiplier=1;internal ZDOID Station;internal string[] Sources=new[]{"chest"};internal List<Need> Needs=new List<Need>{new Need("Wood",10)};
  internal static bool Valid=true;internal static int Validations;
  internal bool Validate(out Player p,out Core c,out string why,bool forceTopology=true){Validations++;p=Player.m_localPlayer;c=new Core();why=Valid?"ok":"network path or coverage changed";return Valid;}
  internal bool SelectNeeds(List<Stock> stock)=>true;
 }
 static class Actions {
  internal class Pending {internal Operation Op;internal Player Player;internal InventoryGui Gui;internal Recipe Recipe;internal ItemDrop.ItemData Upgrade;internal int UpgradeQuality,Variant;internal bool Multi,Prepared;internal float Started,LastPoll;internal RecoveryAttempt Recovery;internal List<Debit> Plan;}
  internal static Pending Active,Waiting;
  internal static Core Context(Player p,bool craft)=>new Core();
  internal static Operation Create(Player p,Core core,bool build,string target,int quality,int amount)=>new Operation{Target=target,Quality=quality,Multiplier=amount,Station=p.station.View.z.m_uid};
  internal static bool Propose(Pending p,List<Debit> plan)=>true;
  internal static void RecoverCraft(Pending p){Waiting=p;}
 }
 static class Topology {internal static void Dirty(){}}
 static class RecipeIndex {internal static string Key(Recipe r)=>r.name+":"+r.Cost;internal static bool Matches(Recipe r,string key)=>Key(r)==key;internal static void Report(string target,string reason){}}
 static class Stockroom {
  internal static List<Need> Requirements(object req,int quality,int amount)=>new List<Need>{new Need("Wood",10*amount)};
  internal static List<Stock> Snapshot(Inventory inv,string source,IEnumerable<Need> needs,bool chest)=>inv.Items.ToList();
  internal static bool Qualities(List<Need> needs,List<Stock> stock,bool craft)=>true;
 }
 static class CraftOverview {
  internal static int Amount=50;
  internal static void Clear(){}internal static void Open(InventoryGui gui,Player p){}internal static void RefreshRows(InventoryGui gui){}
  internal static List<Stock> Stock(Player p,IEnumerable<Need> needs)=>p.inventory.Items.Concat(new[]{new Stock("chest","Wood",1,Amount)}).ToList();
 }
 static class CraftInspection {internal static bool Ready=true;internal static void Clear(){}internal static void Ensure(Actions.Pending p,Core c){}}
 class Transport {
  internal static Transport Instance=new Transport();internal List<Operation> Begins=new List<Operation>();internal int Claims,Uses,Drops;
  internal void Begin(Operation op)=>Begins.Add(op);internal void DropQuote(string id)=>Drops++;internal void HoldQuote(string id)=>Claims++;internal void UseQuote(string id)=>Uses++;
 }
}
static class CraftPreparationRuntimeTests {
 static int passed;static Player player;static InventoryGui gui;
 static void Check(bool value,string why){if(!value)throw new Exception(why);}
 static void Test(string name,Action body){Time.unscaledTime=0;player=Player.m_localPlayer=new Player();gui=new InventoryGui();gui.m_selectedRecipe.Recipe=new Recipe();InventoryGui.Visible=true;Actions.Active=Actions.Waiting=null;Operation.Valid=true;Operation.Validations=0;Transport.Instance=new Transport();CraftOverview.Amount=50;CraftInspection.Ready=true;CraftPreparation.Clear();gui.UpdateRecipe(player,0);body();passed++;Console.WriteLine("PASS preparation runtime "+name);}
 static void Click(){gui.OnCraftPressed();CraftPreparation.Tick();}
 static void Offer(){var op=Transport.Instance.Begins.Last();CraftPreparation.Offered(op.Id,new List<Debit>{new Debit("chest","Wood",1,10)},9);}
 public static int Main(){try{
  Test("viewing a recipe never acquires a chest reservation",()=>{for(int i=0;i<100;i++){Time.unscaledTime+=.1f;gui.UpdateRecipe(player,0);CraftPreparation.Tick();}Check(Transport.Instance.Begins.Count==0,"browsing reserved stock");});
  Test("click prepares once and starts animation only after confirmation",()=>{Click();Check(gui.Starts==0&&Transport.Instance.Begins.Count==1,"craft ran before reservation");Offer();Check(gui.Starts==1&&Transport.Instance.Claims==1,"confirmed click did not start once");});
  Test("click does not wait six seconds for unrelated inspection",()=>{CraftInspection.Ready=false;Click();Check(Time.unscaledTime==0&&Transport.Instance.Begins.Count==1&&gui.Starts==0,"broad inspection blocked payment or skipped confirmation");Offer();Check(gui.Starts==1&&Transport.Instance.Claims==1,"confirmed craft still waited for inspection");});
  Test("unconfirmed displayed stock never starts craft on its own",()=>{CraftInspection.Ready=false;Click();for(int i=1;i<=5;i++){Time.unscaledTime=i;CraftPreparation.Tick();}Check(gui.Starts==0&&Transport.Instance.Claims==0,"preview count authorized craft without owner confirmation");});
  Test("repeated clicks while preparing cannot duplicate payment",()=>{Click();gui.UpdateRecipe(player,0);gui.OnCraftPressed();CraftPreparation.Tick();Check(Transport.Instance.Begins.Count==1,"duplicate request");Offer();Offer();Check(gui.Starts==1,"duplicate offer restarted animation");});
  Test("switching recipe prevents delayed offer from starting another craft",()=>{Click();gui.m_selectedRecipe.Recipe=new Recipe{name="other"};gui.UpdateRecipe(player,0);Offer();Check(gui.Starts==0&&Transport.Instance.Drops==1,"delayed confirmation crafted wrong recipe");});
  Test("closing station releases pending reservation",()=>{Click();InventoryGui.Visible=false;CraftPreparation.Selection(gui,player);Offer();Check(gui.Starts==0&&Transport.Instance.Drops==1,"closed menu resumed");});
  Test("other mod's recipe veto remains respected",()=>{Click();gui.NativeAllowed=false;Offer();Check(gui.Starts==0&&Transport.Instance.Drops==1,"native/modded checks bypassed");});
  Test("recipe changed during confirmation cannot auto-start",()=>{Click();gui.OnUpdate=()=>gui.m_selectedRecipe.Recipe=new Recipe{name="changed"};Offer();Check(gui.Starts==0,"selection changed during callback");});
  Test("personal inventory craft does not contact the network",()=>{player.inventory.Items.Add(new Stock("player","Wood",1,10));gui.UpdateRecipe(player,0);Click();Check(gui.Starts==1&&Transport.Instance.Begins.Count==0,"local craft reserved chest");});
  Test("new personal materials while awaiting reservation can start local craft",()=>{Click();player.inventory.Items.Add(new Stock("player","Wood",1,10));Offer();Check(gui.Starts==1&&Transport.Instance.Claims==0&&Transport.Instance.Drops==1,"local material fallback broken");});
  Test("background coverage fluctuation does not cancel pinned animation",()=>{Click();Offer();Operation.Valid=false;Time.unscaledTime=1;CraftPreparation.Tick();Check(Transport.Instance.Drops==0,"background check cancelled pinned craft");});
  Test("final native completion consumes the reservation once",()=>{Click();Offer();Check(!CraftPreparation.Execute(gui,player)&&Transport.Instance.Uses==1&&Actions.Waiting!=null,"payment not initiated");Check(!CraftPreparation.Claimed,"claim not consumed");});
  Test("changed recipe costs invalidate a prepared offer",()=>{Click();gui.m_selectedRecipe.Recipe.Cost=20;Offer();Check(gui.Starts==0&&Transport.Instance.Drops==1,"old cost accepted");});
  Test("preparation has a bounded wait",()=>{Click();Time.unscaledTime=9;CraftPreparation.Tick();Check(Transport.Instance.Drops==1&&!CraftPreparation.HasReservation,"pending request waited forever");});
  Console.WriteLine("Preparation runtime tests: "+passed+" passed");return 0;
 }catch(Exception e){Console.Error.WriteLine(e);return 1;}}
}
#endif
