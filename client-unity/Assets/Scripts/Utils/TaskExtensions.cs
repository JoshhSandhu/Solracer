using System;
using System.Threading.Tasks;
using UnityEngine;

namespace Solracer.Utils
{
    public static class TaskExtensions
    {
        public static async void FireAndForget(this Task task)
        {
            try
            {
                await task;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[FireAndForget] Unhandled exception: {ex}");
            }
        }
    }
}
