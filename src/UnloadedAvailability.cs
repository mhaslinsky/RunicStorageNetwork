namespace RunicStorageNetwork {
 // The only optional-protocol handler on an opted-out server. No inventory,
 // topology, subscription or experimental initialization is needed to say no.
 internal static class UnloadedAvailability {
  internal static void Decline(long sender,ZPackage packet){
   if(!ZNet.instance||!ZNet.instance.IsServer()||sender==ZNet.GetUID()||packet==null||packet.Size()<4)return;
   int token=packet.ReadInt();if(token<1)return;
   var reply=new ZPackage();reply.Write(token);reply.Write(false);Transport.Send(sender,"unloaded_catalog",reply);
  }
 }
}
