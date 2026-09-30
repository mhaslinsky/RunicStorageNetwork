using System;
using UnityEngine;

namespace RunicStorageNetwork.API {
 public sealed class ApiContext {
  public ZDOID ConsumerId {get;} public string ModId {get;}
  public ApiContext(ZDOID consumerId,string modId){ConsumerId=consumerId;ModId=modId;}
 }
 // Declaration on the registered prefab, on every peer. No per-machine Update.
 public sealed class NetworkConsumer:MonoBehaviour {public string ModId;}
 public static class NetworkResources {
  public const int ContractVersion=1;
  public static ResourceSnapshot GetResources(ApiContext context)=>ApiRuntime.Read(context,null);
  public static ResourceAmountResult GetResourceAmount(ApiContext context,ResourceKey resource)=>ApiRuntime.Amount(context,resource);
  public static ConsumeOperation TryConsumeResources(ApiContext context,ConsumeRequest request,Action<ConsumeResult> completed=null)=>ApiRuntime.Consume(context,request,completed);
 }
}
