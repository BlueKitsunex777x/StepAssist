using BepInEx;
using UnityEngine;
using BlockStoryCore;
using BlockStoryMod;
using HarmonyLib;
using System;


// Turn auto-jump into step-assist
[BepInPlugin("com.yourname.blockstory.stepassist", "Block Story - StepAssist", "1.0.0")]
[BepInDependency(Core.Guid)]               // hard dependency on the toolbox; won't load without it
public class StepAssistPlugin : BaseUnityPlugin
{
    private static bool Enabled = PlayerPrefs.GetInt("StepAssist_Enabled", 1) != 0;

    private void Awake()
    {
        new Harmony("com.blue.blockstory.stepup").PatchAll();

        // registers the mod so it shows on the in-game Mods page
        ModRegistry.Register(new ModInfo
        {
            Name = "StepAssist",
            Description = "Turn auto-jump into step-assist",
            GetEnabled = () => Enabled,
            SetEnabled = on => { Enabled = on; PlayerPrefs.SetInt("StepAssist_Enabled", on ? 1 : 0); PlayerPrefs.Save(); L.Info($"enabled = {on}"); },
            HasConfig = false,
        });
        Logger.LogInfo("step up loaded");
    }


    // the player doesn't use unity's collider on terrain, it uses the game's own box-vs-blocks one
    // (TerrainCharacterController), so stepOffset does nothing on blocks. we lift the box ourselves
    // just before the game moves it sideways into a 1 block ledge
    [HarmonyPatch(typeof(TerrainCharacterController), "Move", new[] { typeof(Vector3), typeof(float) })]
    internal static class StepUpPatch
    {
        // a hair over one block so the feet clear the edge instead of scraping it
        private const float StepHeight = 1.02f;

        // our own lift calls Move again, don't let that trigger another step
        private static bool _lifting;

        private static void Prefix(TerrainCharacterController __instance, Vector3 deltaMove, float deltaTime)
        {
            if (_lifting || !__instance.CompareTag("Player")) return;
            try
            {
                CharacterMotor motor = __instance.GetComponent<CharacterMotor>();
                if (motor == null) return;

                // the game's auto jump would hop over the ledge before we step, so it has to be off
                motor.jumping.autojump = false;

                if (!motor.IsGrounded() || motor.IsClimbing()) return;

                Vector3 sideways = new Vector3(deltaMove.x, 0f, deltaMove.z);
                if (sideways.sqrMagnitude < 0.00001f) return;
                Vector3 direction = sideways.normalized;

                Vector3 position = __instance.transform.position;
                Vector3 halfSize = Vector3.Scale(__instance.eRadius, __instance.transform.lossyScale);

                // the box is centred on the transform, so feet are half the height below it
                float feetY = position.y - halfSize.y;
                int feetBlock = Mathf.FloorToInt(feetY + 0.01f);
                int headBlocks = Mathf.CeilToInt(halfSize.y * 2f);

                // look just past the front edge of the box
                Vector3 ahead = position + direction * (Mathf.Max(halfSize.x, halfSize.z) + 0.1f);
                Vector3 move = direction * 0.1f;
                int aheadX = Mathf.FloorToInt(ahead.x);
                int aheadZ = Mathf.FloorToInt(ahead.z);

                var world = MasterDatabase.instance.terrain.world;

                // only a single block ledge, a two high wall should still stop you
                if (!world[aheadX, feetBlock, aheadZ].IsSolid) return;
                for (int up = 1; up <= headBlocks; up++)
                    if (world[aheadX, feetBlock + up, aheadZ].IsSolid) return;

                // room above our own head too, or we'd pop into the ceiling
                int hereX = Mathf.FloorToInt(position.x);
                int hereZ = Mathf.FloorToInt(position.z);
                if (world[hereX, feetBlock + headBlocks, hereZ].IsSolid) return;

                float lift = (feetBlock + StepHeight) - feetY;
                if (lift <= 0f || lift > StepHeight + 0.1f) return;

                // move up through the game's own collision so it still can't push us into a block
                _lifting = true;
                __instance.Move(new Vector3(direction.x, lift, direction.z), deltaTime);
                L.Warn($"{ahead.x} {position.x}, {lift}, {ahead.z} {position.z} {direction}");
            }
            catch (Exception e) { Debug.LogError("[StepUp] " + e); }
            finally { _lifting = false; }
        }
    }
}
