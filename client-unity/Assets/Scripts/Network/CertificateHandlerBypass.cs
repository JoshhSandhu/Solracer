using UnityEngine;
using UnityEngine.Networking;

namespace Solracer.Network
{
    /// <summary>
    /// Certificate handler that accepts all certificates (for development only)
    /// WARNING: Only use this for local development with self-signed certificates
    /// </summary>
    public class CertificateHandlerBypass : CertificateHandler
    {
        protected override bool ValidateCertificate(byte[] certificateData)
        {
            // Accept all certificates (for self-signed local development)
            Debug.Log("[CertificateHandlerBypass] Accepting certificate (bypass enabled)");
            return true;
        }

        //protected override void Dispose(bool disposing)
        //{
        //    base.Dispose(disposing);
        //}
    }
}




