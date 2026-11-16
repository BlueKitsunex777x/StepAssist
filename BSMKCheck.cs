using System;
using System.IO;
using System.Security.Cryptography;
using BepInEx;
using BepInEx.Bootstrap;
using UnityEngine;

// The BSMK check. Every mod on the BSMK store compiles this file in and calls it first thing in Awake:
//
//     if (!BSMKCheck.Passed(this)) return;
//
// It passes only when the mod and BlockStoryCore were both loaded by BepInEx itself from BepInEx/plugins,
// and the Core file carries our signature. Look-alike Cores (same class names, other code) don't have the
// signature, and other loaders that host our files from their own folder fail the folder check.
// On a fail the mod switches itself off and a small notice says why. It never touches files or saves.
//
// Kept to one self-contained file so other authors can drop the same copy into their mod unchanged.
internal static class BSMKCheck
{
    public const string Message = "This mod was made for BSMK and only runs with the BSMK Mod Kit.";

    // public half of the key the Core is signed with. The private half never leaves MR C's machine
    const string CoreKey = "uHs9UP3a3cM3nKcQO9/qHRUWa/h6DWoUQZh9oNH8hz7srJtc+AOJmgfLNEo0zoydMWzEFS0V5NEO40aQSRZPXsuo5YeP6ZI1jRByO66SmLP3dE/pHBF+82gx0tqaD/3PhES26xH0j4Ts8i/Wr9mHQ1mrsuFN4YYt19TAbsmHC40C9snTHW+dxtQEBmQkzuwK4+P3A3WFzxtL+TiNrc+FRBkDgjngCsZoFRvGiyMJnefzhcaPbuKIH/nv8YF+WKJi+MyvXOdkLup97FBPx9/HChaMpXPfiWLvGP2emzw3FHNM16EJ6dIo9UEfgNUW5s/w895aJ1eBOuGToWvrQJLXhQ==";
    const string Magic = "BSMKSIG1";
    const int SigLen = 256;
    const string CoreGuid = "com.mrc.blockstory.core";

    public static bool Passed(BaseUnityPlugin mod)
    {
        string why;
        // a crash in here counts as a fail on purpose, otherwise breaking the check would be a way past it
        try { why = Problem(mod, true); }
        catch (Exception e) { why = "check crashed: " + e.Message; }
        if (why == null) return true;
        Refuse(mod, why);
        return false;
    }

    // Core's own version: it can't vouch for its own signature, so it only checks where it was loaded from
    public static bool CoreLoadedNormally(BaseUnityPlugin core)
    {
        string why;
        try { why = Problem(core, false); }
        catch (Exception e) { why = "check crashed: " + e.Message; }
        if (why == null) return true;
        Refuse(core, why);
        return false;
    }

    static string Problem(BaseUnityPlugin mod, bool checkCore)
    {
        string plugins = Full(Paths.PluginPath);
        if (string.IsNullOrEmpty(plugins)) return "no BepInEx plugins folder";
        plugins = plugins.TrimEnd('/', '\\') + Path.DirectorySeparatorChar;

        string mine = Full(mod.GetType().Assembly.Location);
        if (!Inside(mine, plugins)) return "mod not loaded from BepInEx/plugins (" + mine + ")";
        string guid = MetadataHelper.GetMetadata(mod)?.GUID;
        if (guid == null || !Chainloader.PluginInfos.TryGetValue(guid, out var self) || !Same(Full(self.Location), mine))
            return "mod not loaded by BepInEx";
        if (!checkCore) return null;

        var coreType = typeof(BlockStoryCore.Core);
        var coreAsm = coreType.Assembly;
        if (coreAsm.GetName().Name != "BlockStoryCore") return "Core is " + coreAsm.GetName().Name;
        string core = Full(coreAsm.Location);
        if (!Inside(core, plugins)) return "Core not loaded from BepInEx/plugins (" + core + ")";
        if (!Chainloader.PluginInfos.TryGetValue(CoreGuid, out var ci) || ci.Instance == null
            || ci.Instance.GetType() != coreType || !Same(Full(ci.Location), core))
            return "Core not loaded by BepInEx";
        if (!Signed(core)) return "Core signature missing or wrong";
        return null;
    }

    static bool Signed(string file)
    {
        byte[] all = File.ReadAllBytes(file);
        int tail = SigLen + Magic.Length;
        if (all.Length <= tail) return false;
        for (int i = 0; i < Magic.Length; i++)
            if (all[all.Length - Magic.Length + i] != (byte)Magic[i]) return false;

        byte[] sig = new byte[SigLen];
        Buffer.BlockCopy(all, all.Length - tail, sig, 0, SigLen);
        byte[] hash;
        using (var sha = SHA256.Create()) hash = sha.ComputeHash(all, 0, all.Length - tail);

        using (var rsa = new RSACryptoServiceProvider())
        {
            rsa.ImportParameters(new RSAParameters { Modulus = Convert.FromBase64String(CoreKey), Exponent = new byte[] { 1, 0, 1 } });
            return rsa.VerifyHash(hash, CryptoConfig.MapNameToOID("SHA256"), sig);
        }
    }

    static string Full(string p) => string.IsNullOrEmpty(p) ? "" : Path.GetFullPath(p);
    static bool Same(string a, string b) => a.Length > 0 && string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
    static bool Inside(string file, string folder) => file.Length > 0 && file.StartsWith(folder, StringComparison.OrdinalIgnoreCase);

    static void Refuse(BaseUnityPlugin mod, string why)
    {
        string name = "a mod";
        try
        {
            name = MetadataHelper.GetMetadata(mod)?.Name ?? mod.GetType().Assembly.GetName().Name;
            if (name.StartsWith("Block Story - ")) name = name.Substring(14);
        }
        catch { }
        try { BepInEx.Logging.Logger.CreateLogSource("BSMK check").LogWarning(name + " not started: " + why + ". " + Message); } catch { }
        try { mod.enabled = false; } catch { }
        try { Notice.Add(name); } catch { }
    }

    // One notice for every mod that didn't start. Each mod has its own copy of this class, so the list of
    // names is passed between them through the AppDomain, and whichever mod fails first draws the box.
    class Notice : MonoBehaviour
    {
        const string Key = "BSMK.check.refused";
        Texture2D _bg;
        bool _closed;

        public static void Add(string name)
        {
            var list = AppDomain.CurrentDomain.GetData(Key) as string;
            AppDomain.CurrentDomain.SetData(Key, string.IsNullOrEmpty(list) ? name : list + ", " + name);
            if (GameObject.Find("BSMK check notice") != null) return;
            var go = new GameObject("BSMK check notice");
            DontDestroyOnLoad(go);
            go.AddComponent<Notice>();
        }

        void OnGUI()
        {
            if (_closed) return;
            if (_bg == null)
            {
                _bg = new Texture2D(1, 1);
                _bg.SetPixel(0, 0, new Color(0.08f, 0.07f, 0.06f, 0.95f));
                _bg.Apply();
            }
            string names = AppDomain.CurrentDomain.GetData(Key) as string ?? "";
            var box = new Rect(Screen.width / 2f - 280, 30, 560, 130);
            GUI.depth = -1000;
            GUI.DrawTexture(box, _bg);
            var title = new GUIStyle(GUI.skin.label) { fontSize = 17, fontStyle = FontStyle.Bold, wordWrap = true, alignment = TextAnchor.UpperCenter };
            title.normal.textColor = new Color(0.95f, 0.78f, 0.35f);
            var body = new GUIStyle(GUI.skin.label) { fontSize = 14, wordWrap = true, alignment = TextAnchor.UpperCenter };
            body.normal.textColor = Color.white;
            GUI.Label(new Rect(box.x + 16, box.y + 12, box.width - 32, 48), Message, title);
            GUI.Label(new Rect(box.x + 16, box.y + 58, box.width - 32, 36), "Not started: " + names, body);
            if (GUI.Button(new Rect(box.x + box.width / 2 - 50, box.y + 96, 100, 26), "OK")) _closed = true;
        }
    }
}
