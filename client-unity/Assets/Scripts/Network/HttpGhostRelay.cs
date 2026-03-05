using System;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;
using Newtonsoft.Json;
using Solracer.Config;

namespace Solracer.Network
{
    public class HttpGhostRelay : IPositionRelay
    {
        private readonly string _baseUrl;
        private readonly string _ghostUpdateUrl;

        public HttpGhostRelay(string baseUrl = null)
        {
            _baseUrl = baseUrl ?? APIConfig.GetApiBaseUrl();
            _ghostUpdateUrl = $"{_baseUrl}/api/v1/ghost/update";
            Debug.Log($"[HttpGhostRelay] Initialized at {_baseUrl}");
        }

        // -----------------------------------------------------------------------
        // IPositionRelay: SendPosition
        // -----------------------------------------------------------------------

        public async Task SendPosition(PositionUpdate update)
        {
            string json = JsonConvert.SerializeObject(update);
            byte[] body = Encoding.UTF8.GetBytes(json);

            using var req = new UnityWebRequest(_ghostUpdateUrl, "POST");
            req.uploadHandler = new UploadHandlerRaw(body);
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");
            req.certificateHandler = new CertificateHandlerBypass();

            var op = req.SendWebRequest();
            while (!op.isDone)
                await Task.Yield();

            if (req.result != UnityWebRequest.Result.Success)
            {
                if (req.responseCode != 403 && req.responseCode != 400)
                    Debug.LogWarning($"[HttpGhostRelay] SendPosition failed: {req.responseCode} {req.error}");
            }
        }

        // -----------------------------------------------------------------------
        // IPositionRelay: GetOpponentPositions
        // -----------------------------------------------------------------------

        public async Task<GhostRaceState> GetOpponentPositions(string raceId)
        {
            string url = $"{_baseUrl}/api/v1/ghost/{Uri.EscapeDataString(raceId)}";

            using var req = UnityWebRequest.Get(url);
            req.certificateHandler = new CertificateHandlerBypass();

            var op = req.SendWebRequest();
            while (!op.isDone)
                await Task.Yield();

            if (req.result != UnityWebRequest.Result.Success)
            {
                if (req.responseCode != 404)
                    Debug.LogWarning($"[HttpGhostRelay] GetOpponentPositions failed: {req.responseCode} {req.error}");
                return null;
            }

            try
            {
                return JsonConvert.DeserializeObject<GhostRaceState>(req.downloadHandler.text);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[HttpGhostRelay] JSON parse error: {ex.Message}");
                return null;
            }
        }
    }
}
