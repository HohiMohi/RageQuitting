using Unity.Netcode.Components;

public class ServerNetworkTransform : NetworkTransform
{
    protected override bool OnIsServerAuthoritative()
    {
        return true;
    }
}
