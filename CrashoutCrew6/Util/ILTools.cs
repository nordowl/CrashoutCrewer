using System.Collections.Generic;
using System.Reflection.Emit;
using HarmonyLib;

namespace CrashoutCrew6
{
    internal static class ILTools
    {
        /// <summary>Reads the integer pushed by a ldc.i4* instruction; returns false if not such an instruction.</summary>
        internal static bool TryGetLdcI4(CodeInstruction ins, out int value)
        {
            value = 0;
            var op = ins.opcode;
            if (op == OpCodes.Ldc_I4) { value = (int)ins.operand; return true; }
            if (op == OpCodes.Ldc_I4_S) { value = (sbyte)ins.operand; return true; }
            if (op == OpCodes.Ldc_I4_0) { value = 0; return true; }
            if (op == OpCodes.Ldc_I4_1) { value = 1; return true; }
            if (op == OpCodes.Ldc_I4_2) { value = 2; return true; }
            if (op == OpCodes.Ldc_I4_3) { value = 3; return true; }
            if (op == OpCodes.Ldc_I4_4) { value = 4; return true; }
            if (op == OpCodes.Ldc_I4_5) { value = 5; return true; }
            if (op == OpCodes.Ldc_I4_6) { value = 6; return true; }
            if (op == OpCodes.Ldc_I4_7) { value = 7; return true; }
            if (op == OpCodes.Ldc_I4_8) { value = 8; return true; }
            if (op == OpCodes.Ldc_I4_M1) { value = -1; return true; }
            return false;
        }

        /// <summary>
        /// In-place rewrite of every "load constant <paramref name="from"/>" into "load constant
        /// <paramref name="to"/>", preserving labels/blocks. Returns how many it changed.
        /// </summary>
        internal static int ReplaceLoadConstant(List<CodeInstruction> code, int from, int to, string context)
        {
            int changed = 0;
            for (int i = 0; i < code.Count; i++)
            {
                if (TryGetLdcI4(code[i], out int v) && v == from)
                {
                    code[i].opcode = OpCodes.Ldc_I4;
                    code[i].operand = to;
                    changed++;
                }
            }
            if (changed == 0)
                Log.Warn($"ILTools: no constant '{from}' found to replace in {context} (game may have changed).");
            else
                Log.Debug($"ILTools: replaced {changed}x constant {from}->{to} in {context}.");
            return changed;
        }

        /// <summary>
        /// Rewrites only "new T[from]" allocations into "new T[to]" (a ldc.i4 &lt;from&gt; immediately
        /// followed by newarr). Leaves other uses of the literal (e.g. enum-compare constants) alone.
        /// </summary>
        internal static int ReplaceNewArrSize(List<CodeInstruction> code, int from, int to, string context)
        {
            int changed = 0;
            for (int i = 0; i < code.Count - 1; i++)
            {
                if (code[i + 1].opcode != OpCodes.Newarr) continue;
                if (TryGetLdcI4(code[i], out int v) && v == from)
                {
                    code[i].opcode = OpCodes.Ldc_I4;
                    code[i].operand = to;
                    changed++;
                }
            }
            if (changed == 0)
                Log.Warn($"ILTools: no 'new[]' of size {from} found in {context} (game may have changed).");
            else
                Log.Debug($"ILTools: resized {changed}x array {from}->{to} in {context}.");
            return changed;
        }
    }
}
