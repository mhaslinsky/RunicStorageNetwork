using System;
using System.Collections.Generic;
using System.Linq;
using RunicStorageNetwork.API;
using RunicStorageNetwork.Logic;

static class ApiTests {
 static int passed;
 static void Check(bool value,string why){if(!value)throw new Exception(why);}
 static void Test(string name,Action body){body();passed++;Console.WriteLine("PASS API "+name);}
 static ConsumeRequest Request(long seq=1,int amount=4,string cycle="cycle")=>new ConsumeRequest("session",seq,cycle,new[]{new ResourceRequirement(new ResourceKey("Wood",1),amount)},ResourceMatchPolicy.AnyMatchingInstance);
 sealed class Backend:IApiPayment {
  internal int Count=20,Paid,ApplyCalls,Restored,Repairs,Captured,Released;internal bool Ready=true,FailCapture,FailAfterDebit,FailSaveOnce,FailRollbackOnce,DelayRelease,FailReleaseOnce;
  internal ApiStatus Before=ApiStatus.Ready;internal bool RepairFixes=true;internal int Steps=1;
  public ApiStatus Prepare()=>Ready?Before:ApiStatus.Updating;
  public void Repair(){Repairs++;if(RepairFixes){Before=ApiStatus.Ready;FailCapture=false;Ready=true;}}
  public void Capture(){Captured++;if(FailCapture)throw new Exception("changed");}
  public bool ApplyNext(){ApplyCalls++;Count-=4;Paid+=4;if(FailAfterDebit)throw new Exception("owner save failed");if(FailSaveOnce){FailSaveOnce=false;throw new Exception("save interrupted");}return ApplyCalls>=Steps;}
  public bool Rollback(){if(FailRollbackOnce){FailRollbackOnce=false;throw new Exception("rollback unavailable");}Count+=Paid;Restored+=Paid;Paid=0;return true;}
  public bool Release(){Released++;if(FailReleaseOnce){FailReleaseOnce=false;throw new Exception("release interrupted");}return !DelayRelease;}
 }
 static void Drive(ApiPayment job,int ticks=300,double start=0){for(int i=0;i<ticks;i++)job.Tick(start+i*.05);}
 public static int Run(){
  Test("normalization merges exact keys and orders stable payload",()=>{var input=new ConsumeRequest("session",1,"cycle",new[]{new ResourceRequirement(new ResourceKey("Iron",2),2),new ResourceRequirement(new ResourceKey("Wood"),3),new ResourceRequirement(new ResourceKey("Iron",2),4)},ResourceMatchPolicy.AnyMatchingInstance);Check(ApiRules.Normalize(input,out var normalized)&&normalized.Resources.Count==2&&normalized.Resources[0].Amount==6,"normalization");});
  Test("quality is exact",()=>Check(!new ResourceKey("Iron",1).Equals(new ResourceKey("Iron",2)),"quality merged"));
  Test("prefab uses ordinal identity",()=>Check(!new ResourceKey("Iron").Equals(new ResourceKey("iron")),"case merged"));
  Test("instance policy must be explicit",()=>Check(!ApiRules.Normalize(new ConsumeRequest("session",1,"c",Request().Resources),out _),"implicit metadata consumption"));
  Test("invalid quantities never admitted",()=>{foreach(int amount in new[]{0,-1,100001})Check(!ApiRules.Normalize(Request(amount:amount),out _),"invalid quantity");});
  Test("duplicate key cannot overflow quantity budget",()=>Check(!ApiRules.Normalize(new ConsumeRequest("s",1,"c",new[]{new ResourceRequirement(new ResourceKey("Iron"),100000),new ResourceRequirement(new ResourceKey("Iron"),1)},ResourceMatchPolicy.AnyMatchingInstance),out _),"overflow"));
  Test("zero and overflowing sequence rejected",()=>{Check(!ApiRules.Normalize(Request(0),out _),"zero");Check(!ApiRules.Normalize(Request(long.MaxValue),out _),"max");});
  Test("enumeration bounded to detect 33rd requirement",()=>Check(!ApiRules.Normalize(new ConsumeRequest("s",1,"c",Enumerable.Range(0,10000).Select(i=>new ResourceRequirement(new ResourceKey("Item"+i),1)),ResourceMatchPolicy.AnyMatchingInstance),out _),"count"));
  Test("DTO cannot mutate input collection",()=>{var list=new List<ResourceRequirement>{new ResourceRequirement(new ResourceKey("Wood"),5)};var r=new ConsumeRequest("s",1,"c",list);list.Clear();Check(r.Resources.Count==1,"alias");bool denied=false;try{((IList<ResourceRequirement>)r.Resources).Clear();}catch(NotSupportedException){denied=true;}Check(denied,"mutable public array");});
  Test("partial zero is unknown",()=>{var r=new ResourceAmountResult(new ResourceSnapshot(ApiStatus.Partial,complete:false),0);Check(r.Amount==null&&r.ObservedAmount==0,"partial zero reported exact");});
  Test("complete zero is exact",()=>Check(new ResourceAmountResult(new ResourceSnapshot(ApiStatus.Ready,complete:true),0).Amount==0,"zero lost"));
  Test("replay returns the original pending operation",()=>{var l=new ApiLedger();l.Admit("machine",7,Request(),0,out var e);Check(l.Admit("machine",7,Request(),1,out var replay)==ApiStatus.Ready&&ReferenceEquals(e,replay)&&l.Count==1,"duplicate admission");});
  Test("different payload under same id conflicts",()=>{var l=new ApiLedger();l.Admit("m",7,Request(),0,out _);Check(l.Admit("m",7,Request(amount:5),0,out _)==ApiStatus.RequestConflict,"payload replaced");Check(l.Admit("m",7,Request(cycle:"new"),0,out _)==ApiStatus.RequestConflict,"cycle replaced");});
  Test("only final released operation permits next sequence",()=>{var l=new ApiLedger();l.Admit("m",7,Request(),0,out var e);Check(l.Admit("m",7,Request(2),0,out _)==ApiStatus.Busy,"pending replaced");l.Finish(e,ConsumptionOutcome.Success,ApiStatus.Ready,false);Check(l.Admit("m",7,Request(2),0,out _)==ApiStatus.Busy,"cleanup discarded");e.Released=true;Check(l.Admit("m",7,Request(2),0,out _)==ApiStatus.Ready,"new cycle blocked");Check(l.Admit("m",7,Request(),0,out _)==ApiStatus.ExpiredRequest,"old sequence debited");});
  Test("new owner gets original receipt",()=>{var l=new ApiLedger();l.Admit("m",7,Request(),0,out var e);l.Finish(e,ConsumptionOutcome.Success,ApiStatus.Ready,true);Check(l.Admit("m",18,Request(),2,out var replay)==ApiStatus.Ready&&replay.Result.Resources[0].Amount==4,"replay lost on owner change");});
  Test("receipt cannot be changed after success",()=>{var l=new ApiLedger();l.Admit("m",1,Request(),0,out var e);l.Finish(e,ConsumptionOutcome.Success,ApiStatus.Ready,false);bool denied=false;try{l.Finish(e,ConsumptionOutcome.NoDebit,ApiStatus.Unavailable,true);}catch(InvalidOperationException){denied=true;}Check(denied,"conflicting outcome");});
  Test("64 operations limit includes cleanup",()=>{var l=new ApiLedger();for(int i=0;i<64;i++)Check(l.Admit("m"+i,i,Request(),0,out _)==ApiStatus.Ready,"early limit");Check(l.Admit("extra",999,Request(),0,out _)==ApiStatus.Busy,"overflow");});
  Test("16 operations per peer",()=>{var l=new ApiLedger();for(int i=0;i<16;i++)l.Admit("m"+i,7,Request(),0,out _);Check(l.Admit("m17",7,Request(),0,out _)==ApiStatus.Busy,"peer limit");});
  Test("bounded receipt history replaces instead of appends",()=>{var l=new ApiLedger();for(int i=1;i<=10000;i++){l.Admit("m",7,Request(i),i,out var e);l.Finish(e,ConsumptionOutcome.Success,ApiStatus.Ready,true);}Check(l.Count==1&&l.Find("m").Request.Sequence==10000,"receipt leak");});
  Test("4096 identities never evicted into a new debit",()=>{var l=new ApiLedger();for(int i=0;i<4096;i++){l.Admit("m"+i,7,Request(),0,out var e);l.Finish(e,ConsumptionOutcome.NoDebit,ApiStatus.NoNetwork,true);}Check(l.Admit("extra",7,Request(),0,out _)==ApiStatus.CapacityExceeded,"unbounded identities");Check(l.Admit("m0",7,Request(),0,out var first)==ApiStatus.Ready&&first.Result.Outcome==ConsumptionOutcome.NoDebit,"old receipt evicted");});
  Test("success consumes exactly once across repeated ticks",()=>{var b=new Backend();var job=new ApiPayment(b,0,10);Drive(job);Check(job.Outcome==ConsumptionOutcome.Success&&job.Released&&b.Count==16&&b.ApplyCalls==1,"duplicate debit");});
  Test("stale network repaired before payment",()=>{var b=new Backend{Before=ApiStatus.NoNetwork};var job=new ApiPayment(b,0,10);Drive(job);Check(job.Outcome==ConsumptionOutcome.Success&&b.Repairs==1&&b.Count==16,"no repair");});
  Test("access cache repaired before refusal",()=>{var b=new Backend{Before=ApiStatus.AccessDenied};var job=new ApiPayment(b,0,10);Drive(job);Check(job.Outcome==ConsumptionOutcome.Success&&b.Repairs==1,"access retry");});
  Test("fresh shortage repaired before refusal",()=>{var b=new Backend{Before=ApiStatus.InsufficientResources};var job=new ApiPayment(b,0,10);Drive(job);Check(job.Outcome==ConsumptionOutcome.Success&&b.Repairs==1,"count retry");});
  Test("capture failure never debits",()=>{var b=new Backend{FailCapture=true,RepairFixes=false};var job=new ApiPayment(b,0,10);Drive(job);Check(job.Outcome==ConsumptionOutcome.NoDebit&&b.Count==20&&b.ApplyCalls==0&&job.Repairs==4,"unsafe capture");});
  Test("unavailable source expires without debit",()=>{var b=new Backend{Ready=false};var job=new ApiPayment(b,0,1);Drive(job);Check(job.Outcome==ConsumptionOutcome.NoDebit&&b.Count==20&&job.Released,"timeout debit");});
  Test("post-debit save failure rolls back own amount",()=>{var b=new Backend{FailSaveOnce=true};var job=new ApiPayment(b,0,10);Drive(job);Check(job.Outcome==ConsumptionOutcome.NoDebit&&b.Count==20&&b.Restored==4&&b.ApplyCalls==1,"rollback duplicated");});
  Test("failed rollback remains recovering and retries original delta",()=>{var b=new Backend{FailAfterDebit=true,FailRollbackOnce=true};var job=new ApiPayment(b,0,1);Drive(job);Check(job.Outcome==ConsumptionOutcome.NoDebit&&b.Count==20&&b.Restored==4&&b.ApplyCalls==1,"rollback retry changed debit");});
  Test("first-write deadline never aborts a paid operation",()=>{var b=new Backend{Steps=3};var job=new ApiPayment(b,0,.1);job.Tick(0);job.Tick(4);job.Tick(5);job.Tick(6);job.Tick(7);Check(job.Outcome==ConsumptionOutcome.Success&&b.Count==8,"deadline discarded payment");});
  Test("success survives delayed cleanup",()=>{var b=new Backend{DelayRelease=true};var job=new ApiPayment(b,0,1);Drive(job,30);Check(job.Outcome==ConsumptionOutcome.Success&&!job.Released&&b.Count==16,"success lost");b.DelayRelease=false;Drive(job,30,3);Check(job.Released&&b.ApplyCalls==1,"cleanup re-debit");});
  Test("shared source gate excludes player and machine simultaneously",()=>{var gate=new SourceGate();Check(gate.TryAcquire("player",new[]{"a"}),"player reserve");Check(!gate.TryAcquire("api",new[]{"a","b"})&&!gate.Held("b"),"partial acquire");gate.Cancel("player");Check(gate.TryAcquire("api",new[]{"a","b"}),"machine reserve");Check(!gate.TryAcquire("player",new[]{"b"}),"player double-use");});
  Test("late old-owner frame rejected even with high content revision",()=>Check(!ApiRules.AcceptSourceFrame(false,7,12,9,11),"old source overwritten"));
  Test("held source rejects current-owner frames too",()=>Check(!ApiRules.AcceptSourceFrame(true,7,12,7,12),"held content overwritten"));
  Test("new legitimate owner may update after release",()=>Check(ApiRules.AcceptSourceFrame(false,9,13,9,13),"ordinary owner blocked"));
  Test("token bucket bounds burst and recovers without catch-up flood",()=>{var b=new ApiBucket(4,4,0);for(int i=0;i<4;i++)Check(b.Take(0),"capacity");Check(!b.Take(0)&&b.Take(.25)&&!b.Take(.25),"rate");for(int i=0;i<4;i++)Check(b.Take(100),"refill");Check(!b.Take(100),"unbounded refill");});
  return passed;
 }
}
