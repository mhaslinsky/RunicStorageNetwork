using System;
using System.Collections.Generic;
using System.Linq;
using RunicStorageNetwork.API;

namespace RunicStorageNetwork.Logic {
 // The production state machine is independent of Unity so fault paths can be tested.
 internal interface IApiPayment {
  ApiStatus Prepare(); // Ready only after every selected owner has handed off fresh stock.
  void Repair();
  void Capture();      // Capture ALL deltas before the first mutation.
  bool ApplyNext();    // true when every delta is saved.
  bool Rollback();     // true only when every removed quantity is saved back.
  bool Release();
 }
 internal sealed class ApiPayment {
  readonly IApiPayment backend;readonly double deadline;int repairs;double next;
  bool writing,rollback,cleaning;ApiStatus last=ApiStatus.SourcesUnavailable;
  internal ConsumptionState State {get;private set;}=ConsumptionState.Pending;
  internal ConsumptionOutcome? Outcome {get;private set;}
  internal ApiStatus Reason {get;private set;}
  internal bool Released {get;private set;}
  internal int Repairs=>repairs;
  internal ApiPayment(IApiPayment backend,double now,double startWithin){this.backend=backend;deadline=now+startWithin;}
  internal void Tick(double now){
   if(Released||now<next)return;
   try {
    if(Outcome.HasValue){Released=backend.Release();if(!Released)next=now+.5;return;}
    if(rollback){State=ConsumptionState.Recovering;if(backend.Rollback()){rollback=false;Finish(ConsumptionOutcome.NoDebit,last);}else next=now+.5;return;}
    if(writing){State=ConsumptionState.Recovering;if(backend.ApplyNext())Finish(ConsumptionOutcome.Success,ApiStatus.Ready);return;}
    if(cleaning){if(!backend.Release()){next=now+.25;return;}cleaning=false;
     if(repairs>=4||now>=deadline){Finish(ConsumptionOutcome.NoDebit,last);return;}
     backend.Repair();repairs++;State=ConsumptionState.Repairing;next=now+(repairs<=2?0:.4);return;
    }
    if(now>=deadline){last=last==ApiStatus.SourcesUnavailable?ApiStatus.StartExpired:last;cleaning=true;return;}
    var result=backend.Prepare();
    if(result==ApiStatus.Updating||result==ApiStatus.Busy||result==ApiStatus.NotReady){next=now+.05;return;}
    if(result!=ApiStatus.Ready){last=result;cleaning=true;return;}
    backend.Capture();writing=true;State=ConsumptionState.Recovering;
   }catch{last=ApiStatus.SourcesUnavailable;if(writing){rollback=true;State=ConsumptionState.Recovering;next=now+.1;}else {cleaning=true;State=ConsumptionState.Repairing;}}
  }
  void Finish(ConsumptionOutcome value,ApiStatus reason){Outcome=value;Reason=reason;State=ConsumptionState.Completed;}
 }
}
