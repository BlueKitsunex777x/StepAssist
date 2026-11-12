using BepInEx;
using UnityEngine;
using BlockStoryCore;
using BlockStoryMod;
using HarmonyLib;
using System;


// Turn auto-jump into step-assist
[BepInPlugin("com.blue.blockstory.stepassist", "Block Story - StepAssist", "1.0.0")]
[BepInDependency(Core.Guid)]               // hard dependency on the toolbox; won't load without it
public class StepAssistPlugin : BaseUnityPlugin
{
    private static bool Enabled = PlayerPrefs.GetInt("StepAssist_Enabled", 1) != 0;

    private void Awake()
    {
        new Harmony("com.blue.blockstory.stepassist").PatchAll();

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

        // how far onto the ledge the step carries you, small enough not to feel like a shove
        private const float StepForward = 0.2f;

        // our own lift calls Move again, don't let that trigger another step
        private static bool _lifting;

        private static void Prefix(TerrainCharacterController __instance, Vector3 deltaMove, float deltaTime)
        {
            if (!Enabled || _lifting || !__instance.CompareTag("Player")) return;
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

                var world = MasterDatabase.instance.terrain.world;

                // check the left edge, middle and right edge of our front, not just the middle.
                // one spot misses corners: walking diagonally into an inside corner it lands on the
                // empty block between them, and clipping an outside corner it misses the block
                Vector3 front = position + direction * (Mathf.Max(halfSize.x, halfSize.z) + 0.1f);
                Vector3 across = new Vector3(-direction.z, 0f, direction.x) * (Mathf.Min(halfSize.x, halfSize.z) * 0.9f);
                Vector3[] spots = { front - across, front, front + across };

                bool ledge = false;
                foreach (Vector3 spot in spots)
                {
                    int spotX = Mathf.FloorToInt(spot.x);
                    int spotZ = Mathf.FloorToInt(spot.z);

                    // anything at head height anywhere across the front means a wall or a low
                    // ceiling, and stepping up there would jam us into it
                    for (int up = 1; up <= headBlocks; up++)
                        if (world[spotX, feetBlock + up, spotZ].IsSolid) return;

                    if (world[spotX, feetBlock, spotZ].IsSolid) ledge = true;
                }
                if (!ledge) return;

                // room above our own head too, or we'd pop into the ceiling
                int hereX = Mathf.FloorToInt(position.x);
                int hereZ = Mathf.FloorToInt(position.z);
                if (world[hereX, feetBlock + headBlocks, hereZ].IsSolid) return;

                float lift = (feetBlock + StepHeight) - feetY;
                if (lift <= 0f || lift > StepHeight + 0.1f) return;

                // move up through the game's own collision so it still can't push us into a block
                _lifting = true;
                __instance.Move(new Vector3(0f, lift, 0f), deltaTime);

                // then a small push the way we're walking, so we land on the ledge instead of
                // hovering at its edge. done as a second move so the up part clears the edge first
                __instance.Move(direction * StepForward, deltaTime);
            }
            catch (Exception e) { Debug.LogError("[StepUp] " + e); }
            finally { _lifting = false; }
        }
    }
}
