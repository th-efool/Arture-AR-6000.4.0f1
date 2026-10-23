using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using UnityEngine;

public class TestTools : MonoBehaviour
{
    // Method names MUST match your JSON schema
    public async Task<JObject> Cube_StartSpin(JObject args)
    {
        // Your logic here
        transform.Rotate(0, 90, 0);

        await Task.Yield();
        return new JObject { ["success"] = true };
    }

    public async Task<JObject> Cube_StopSpin(JObject args)
    {
        // Your logic here
        transform.rotation = Quaternion.identity;

        await Task.Yield();
        return new JObject { ["success"] = true };
    }

    public async Task<JObject> Cube_SetSpeed(JObject args)
    {
        // Extract parameters from args
        var speed = args?.Value<float?>("speed") ?? 90f;

        // Your logic here
        Debug.Log($"Setting speed to {speed}");

        await Task.Yield();
        return new JObject { ["success"] = true, ["speed"] = speed };
    }
}