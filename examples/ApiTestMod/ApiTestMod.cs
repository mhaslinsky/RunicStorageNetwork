using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx;
using Jotunn.Configs;
using Jotunn.Entities;
using Jotunn.Managers;
using UnityEngine;
using RunicStorageNetwork.API;

namespace RunicStorageNetwork.ApiTest {
 [BepInPlugin(Id,"RSN API local test","0.1.0")]
 [BepInDependency("local.runicstoragenetwork","0.8.8")]
 [BepInDependency("com.jotunn.jotunn","2.30.2")]
 public sealed class ApiTestPlugin:BaseUnityPlugin {
  public const string Id="local.runicstoragenetwork.apitest",Prefab="RSN_ApiTestConsumer";
  internal static ApiTestPlugin Instance;internal static readonly HashSet<ApiTestConsumer> Pieces=new HashSet<ApiTestConsumer>();
  double next;
  void Awake(){Instance=this;PrefabManager.OnVanillaPrefabsAvailable+=Register;new Terminal.ConsoleCommand("rsn_api_test","RSN API test: status | list | amount Wood [quality] | pay Wood 5 [quality] | retry",Command);}
  void OnDestroy(){PrefabManager.OnVanillaPrefabsAvailable-=Register;if(Instance==this)Instance=null;}
  void Register(){
   PrefabManager.OnVanillaPrefabsAvailable-=Register;
   var prefab=PrefabManager.Instance.CreateClonedPrefab(Prefab,"piece_workbench");prefab.SetActive(false);
   var station=prefab.GetComponent<CraftingStation>();if(station)UnityEngine.Object.DestroyImmediate(station);
   prefab.AddComponent<NetworkConsumer>().ModId=Id;prefab.AddComponent<ApiTestConsumer>();
   var config=new PieceConfig{Name="API Test Consumer",Description="Local test only. Consumes resources into a test counter.",PieceTable="Hammer",Category="Crafting",Requirements=new[]{new RequirementConfig("Wood",1,0,true)}};
   if(!PieceManager.Instance.AddPiece(new CustomPiece(prefab,false,config)))throw new InvalidOperationException("Cannot register API test piece");prefab.SetActive(true);
  }
  internal static bool LocalOnly()=>ZNet.instance&&ZNet.instance.IsServer()&&Player.m_localPlayer&&ZNet.instance.GetPeers().Count==0;
  internal static void Log(string message){Instance?.Logger.LogInfo("[RSN API test] "+message);}
  void Update(){if(Time.realtimeSinceStartupAsDouble<next)return;next=Time.realtimeSinceStartupAsDouble+.5;
   if(!LocalOnly())return;foreach(var piece in Pieces.ToArray())if(piece&&piece.View&&piece.View.IsOwner())try{piece.Tick();}catch(Exception e){Logger.LogError(e);}
  }
  void Command(Terminal.ConsoleEventArgs args){
   Action<string> print=s=>args.Context.AddString(s);
   if(!LocalOnly()){print("This test consumer is for a solo local world. Do not use it on a multiplayer server.");return;}
   var player=Player.m_localPlayer;var piece=Pieces.Where(p=>p&&p.View&&p.View.IsValid()&&Vector3.Distance(p.transform.position,player.transform.position)<8).OrderBy(p=>Vector3.Distance(p.transform.position,player.transform.position)).FirstOrDefault();
   if(!piece){print("Build API Test Consumer (Hammer / Crafting) and stand within 8 m.");return;}
   var words=args.Args;string command=words.Length>1?words[1]:"status";
   try{
    var snap=NetworkResources.GetResources(piece.Context);
    if(command=="status"){var state=piece.Read();print("API="+snap.Status+" network="+snap.NetworkId+" session="+snap.SessionId+" revision="+snap.Revision+" phase="+state.Phase+" sequence="+state.Sequence);foreach(var row in state.Credits)print("Paid input credit: "+row.Key.Replace('\n','@')+" = "+row.Value);return;}
    if(command=="list"){print(snap.Status+"; complete="+snap.IsComplete+" unknown sources="+snap.UnknownSources);foreach(var row in snap.Resources.Take(80))print(row.Resource+" = "+row.ObservedAmount);return;}
    if(command=="amount"&&words.Length>=3){int quality=words.Length>=4?int.Parse(words[3]):1;var result=NetworkResources.GetResourceAmount(piece.Context,new ResourceKey(words[2],quality));print(result.Status+" exact="+(result.Amount?.ToString()??"unknown")+" observed="+(result.ObservedAmount?.ToString()??"unknown"));return;}
    if(command=="pay"&&words.Length>=4){int amount=int.Parse(words[3]),quality=words.Length>=5?int.Parse(words[4]):1;if(amount<1||amount>1000||quality<1||quality>10000)throw new ArgumentException("amount 1..1000; quality 1..10000");
     piece.Begin(words[2],quality,amount,snap);print("Test intent saved. Resources will be consumed into a counter, not spawned as items. Use status to inspect.");return;
    }
    if(command=="retry"){var state=piece.Read();if(state.Sequence==0||state.Session!=snap.SessionId){print("No request from this server session to replay.");return;}var op=NetworkResources.TryConsumeResources(piece.Context,state.Request());print("Replay: "+op.AdmissionCode+" "+op.State+" "+op.Result?.Outcome+"; input counter remains unchanged.");return;}
    print("rsn_api_test status | list | amount Wood [quality] | pay Wood 5 [quality] | retry");
   }catch(Exception e){print("Test command: "+e.Message);}
  }
 }
 public sealed class ApiTestConsumer:MonoBehaviour,Hoverable,Interactable {
  const string StateKey="rsn_apitest_state_v1";internal ZNetView View;double next;
  internal ApiContext Context=>new ApiContext(View.GetZDO().m_uid,ApiTestPlugin.Id);
  void Start(){View=GetComponent<ZNetView>();if(View&&View.IsValid())ApiTestPlugin.Pieces.Add(this);}
  void OnDestroy()=>ApiTestPlugin.Pieces.Remove(this);
  public string GetHoverName()=>"API Test Consumer";
  public float GetHoverOffset()=>1.2f;
  public string GetHoverText()=>"API Test Consumer\nUse console: rsn_api_test status / list / pay Wood 5";
  public bool Interact(Humanoid user,bool hold,bool alt){if(!hold)ApiTestPlugin.Log("Use rsn_api_test list; rsn_api_test pay Wood 5; rsn_api_test status; rsn_api_test retry");return !hold;}
  public bool UseItem(Humanoid user,ItemDrop.ItemData item)=>false;
  internal sealed class State {
   internal string Phase="Idle",Session="",Cycle="",Item="";internal int Quality=1,Amount;internal long Sequence;internal readonly Dictionary<string,long> Credits=new Dictionary<string,long>();
   internal ConsumeRequest Request()=>new ConsumeRequest(Session,Sequence,Cycle,new[]{new ResourceRequirement(new ResourceKey(Item,Quality),Amount)},ResourceMatchPolicy.AnyMatchingInstance);
  }
  internal State Read(){
   var state=new State();var bytes=View.GetZDO().GetByteArray(StateKey);if(bytes==null||bytes.Length==0)return state;
   var p=new ZPackage(bytes);if(p.ReadInt()!=1)throw new InvalidOperationException("Test state version");state.Phase=p.ReadString();state.Session=p.ReadString();state.Cycle=p.ReadString();state.Item=p.ReadString();state.Quality=p.ReadInt();state.Amount=p.ReadInt();state.Sequence=p.ReadLong();int count=p.ReadInt();if(count<0||count>64)throw new InvalidOperationException("Test buffer limit");for(int i=0;i<count;i++)state.Credits.Add(p.ReadString(),p.ReadLong());return state;
  }
  void Save(State state){
   if(!View.IsOwner()||!ZNet.instance.IsServer())throw new InvalidOperationException("Test buffer requires authoritative owner");
   var p=new ZPackage();p.Write(1);p.Write(state.Phase);p.Write(state.Session);p.Write(state.Cycle);p.Write(state.Item);p.Write(state.Quality);p.Write(state.Amount);p.Write(state.Sequence);p.Write(state.Credits.Count);foreach(var row in state.Credits){p.Write(row.Key);p.Write(row.Value);}View.GetZDO().Set(StateKey,p.GetArray());
  }
  internal void Begin(string item,int quality,int amount,ResourceSnapshot snap){
   var s=Read();if(s.Phase!="Idle"&&s.Phase!="Applied")throw new InvalidOperationException("Previous intent is still "+s.Phase);
   if(snap.Status!=ApiStatus.Ready&&snap.Status!=ApiStatus.Partial)throw new InvalidOperationException("Wait for API: "+snap.Status);
   if(s.Credits.Count>=64&&!s.Credits.ContainsKey(item+"\n"+quality))throw new InvalidOperationException("Test input buffer full");
   s.Item=item;s.Quality=quality;s.Amount=amount;s.Cycle=Guid.NewGuid().ToString("N");s.Session=snap.SessionId;s.Sequence++;s.Phase="Pending";Save(s);next=0;
  }
  internal void Tick(){
   if(Time.realtimeSinceStartupAsDouble<next)return;next=Time.realtimeSinceStartupAsDouble+1;var s=Read();
   if(s.Phase=="Idle"||s.Phase=="Applied"||s.Phase=="RecoveryRequired")return;
   if(s.Phase=="Paid"){
    string key=s.Item+"\n"+s.Quality;s.Credits.TryGetValue(key,out long old);s.Credits[key]=checked(old+s.Amount);s.Phase="Applied";
    // Credit and Applied marker are one authoritative value. A repeated tick cannot apply twice.
    Save(s);ApiTestPlugin.Log("Applied "+s.Amount+" "+s.Item+"; total input credit="+s.Credits[key]);return;
   }
   var snap=NetworkResources.GetResourceAmount(Context,new ResourceKey(s.Item,s.Quality));if(string.IsNullOrEmpty(snap.SessionId))return;
   if(s.Session!=snap.SessionId){s.Phase="RecoveryRequired";Save(s);ApiTestPlugin.Log("Unresolved intent belongs to another server session. No new debit or output is allowed.");return;}
   if(s.Phase=="Waiting"){
    if(snap.Status!=ApiStatus.Ready||snap.Amount<s.Amount||!snap.Amount.HasValue)return;
    s.Sequence++;s.Phase="Pending";Save(s);
   }
   // Restart/reconnect reuses the persisted identity and original payload.
   var operation=NetworkResources.TryConsumeResources(Context,s.Request());if(!operation.IsFinal)return;
   if(operation.Result?.Outcome==ConsumptionOutcome.Success){s.Phase="Paid";Save(s);return;}
   if(operation.Result?.Outcome==ConsumptionOutcome.NoDebit){s.Phase="Waiting";Save(s);ApiTestPlugin.Log("No debit: "+operation.Result.Reason+". Waiting for resources/network before the next sequence.");return;}
   if(operation.Result?.Outcome==ConsumptionOutcome.OutcomeUnknown||operation.AdmissionCode==ApiStatus.RequestConflict||operation.AdmissionCode==ApiStatus.ExpiredRequest){s.Phase="RecoveryRequired";Save(s);ApiTestPlugin.Log("Outcome unresolved; preserving request "+operation.RequestId);}
  }
 }
}
