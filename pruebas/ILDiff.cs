// Compara dos compilaciones de StreamDelay instrucción por instrucción (IL), clase por clase.
// Los nombres de variables locales no existen en el IL, así que renombrar variables o añadir
// comentarios no cambia nada; cualquier cambio de lógica sí aparece como diferencia.
// Uso: ildiff.exe A.exe B.exe [Clase1,Clase2,...]
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Text;
using System.Text.RegularExpressions;

static class ILDiff
{
    static readonly Dictionary<short, OpCode> Ops = new Dictionary<short, OpCode>();
    const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

    static int Main(string[] a)
    {
        foreach (var f in typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static))
        {
            var op = (OpCode)f.GetValue(null);
            Ops[op.Value] = op;
        }
        var A = Assembly.LoadFile(Path.GetFullPath(a[0]));
        var B = Assembly.LoadFile(Path.GetFullPath(a[1]));
        HashSet<string> only = a.Length > 2 ? new HashSet<string>(a[2].Split(',')) : null;
        var ta = Types(A); var tb = Types(B);
        int same = 0, diff = 0;
        foreach (var name in ta.Keys.Union(tb.Keys).OrderBy(x => x))
        {
            if (only != null && !only.Contains(name.Split('+')[0].Replace("StreamDelay.", ""))) continue;
            Dictionary<string, string> ma, mb;
            ta.TryGetValue(name, out ma); tb.TryGetValue(name, out mb);
            if (ma == null || mb == null) { Console.WriteLine("DIFERENTE  " + name + "  (solo existe en " + (ma == null ? "B" : "A") + ")"); diff++; continue; }
            var bad = new List<string>();
            foreach (var k in ma.Keys.Union(mb.Keys).OrderBy(x => x))
            {
                string x, y;
                ma.TryGetValue(k, out x); mb.TryGetValue(k, out y);
                if (x != y) bad.Add(k + (x == null ? " (solo en B)" : y == null ? " (solo en A)" : FirstDiff(x, y)));
            }
            if (bad.Count == 0) { Console.WriteLine("IDÉNTICA   " + name + "  (" + ma.Count + " miembros)"); same++; }
            else { Console.WriteLine("DIFERENTE  " + name + "  (" + bad.Count + " de " + Math.Max(ma.Count, mb.Count) + ")"); foreach (var b in bad.Take(8)) Console.WriteLine("           · " + b); diff++; }
        }
        Console.WriteLine("Clases idénticas: " + same + " · diferentes: " + diff);
        return diff == 0 ? 0 : 1;
    }

    static string FirstDiff(string x, string y)
    {
        var lx = x.Split('\n'); var ly = y.Split('\n');
        for (int i = 0; i < Math.Max(lx.Length, ly.Length); i++)
        {
            string p = i < lx.Length ? lx[i] : "∅", q = i < ly.Length ? ly[i] : "∅";
            if (p != q) return "\n               A: " + Cut(p) + "\n               B: " + Cut(q);
        }
        return "";
    }
    static string Cut(string s) { return s.Length > 140 ? s.Substring(0, 140) + "…" : s; }

    // Nombres que pone el compilador (clases de lambdas, etc.): el número no importa.
    static string Norm(string s)
    {
        if (s == null) return "";
        s = Regex.Replace(s, @"(CachedAnonymousMethodDelegate|DisplayClass|>b__|>c__)[0-9a-f]+(_[0-9a-f]+)?", "$1#");
        // Tablas internas de los switch sobre textos: GUID aleatorio en cada compilación.
        s = Regex.Replace(s, @"<PrivateImplementationDetails>\{[0-9A-Fa-f-]+\}", "<PrivateImplementationDetails>");
        s = Regex.Replace(s, @"\$\$method0x[0-9a-f]+-\d+", "$$$$switch");
        return Regex.Replace(s, @"(<[^>]*>[\w$]*?|CS\$<>[\w]*?|<>\w*?)(\d+)(_\d+)?", m => m.Groups[1].Value + "#");
    }

    static bool Generated(Type t) { for (; t != null; t = t.DeclaringType) if (t.Name.Contains("<") || t.Name.StartsWith("CS$")) return true; return false; }

    static Dictionary<string, Dictionary<string, string>> Types(Assembly asm)
    {
        Type[] types;
        try { types = asm.GetTypes(); } catch (ReflectionTypeLoadException e) { types = e.Types.Where(t => t != null).ToArray(); }
        var res = new Dictionary<string, Dictionary<string, string>>();
        foreach (var t in types)
        {
            if (Generated(t)) continue; // su código se compara dentro del método que lo usa
            var m = new Dictionary<string, string>();
            m["#base"] = Norm(t.BaseType != null ? t.BaseType.FullName : "") + " : " + string.Join(",", t.GetInterfaces().Select(i => i.FullName).OrderBy(x => x));
            m["#campos"] = string.Join("\n", t.GetFields(All).Where(f => !f.Name.Contains("<")).Select(f => (f.IsStatic ? "static " : "") + (f.IsLiteral ? "const=" + f.GetRawConstantValue() + " " : "") + f.FieldType.FullName + " " + f.Name).OrderBy(x => x));
            foreach (var mb in t.GetMethods(All).Cast<MethodBase>().Concat(t.GetConstructors(All)))
            {
                if (mb.Name.Contains("<")) continue; // lambdas: se comparan dentro de su método
                string key = mb.Name + "(" + string.Join(",", mb.GetParameters().Select(p => p.ParameterType.FullName)) + ")";
                m[key] = Body(mb, new HashSet<MethodBase>());
            }
            res[t.FullName] = m;
        }
        return res;
    }

    static string Body(MethodBase mb, HashSet<MethodBase> seen)
    {
        if (!seen.Add(mb)) return "(recursivo)";
        MethodBody body;
        try { body = mb.GetMethodBody(); } catch { return "(sin cuerpo)"; }
        if (body == null) return "(sin cuerpo)";
        byte[] il = body.GetILAsByteArray();
        var module = mb.Module;
        Type[] targs = mb.DeclaringType != null && mb.DeclaringType.IsGenericType ? mb.DeclaringType.GetGenericArguments() : null;
        Type[] margs = mb.IsGenericMethod ? mb.GetGenericArguments() : null;
        // Primera pasada: posición de cada instrucción (para convertir saltos en número de instrucción).
        var starts = new List<int>();
        var lines = new List<string>();
        var branches = new List<KeyValuePair<int, int>>();
        var localMap = new Dictionary<int, int>();
        Func<int, string> loc = i => { int v; if (!localMap.TryGetValue(i, out v)) { v = localMap.Count; localMap[i] = v; } return "L" + v + ":" + Norm(i < body.LocalVariables.Count ? body.LocalVariables[i].LocalType.FullName : "?"); };
        int pos = 0;
        while (pos < il.Length)
        {
            starts.Add(pos);
            short code = il[pos++];
            if (code == 0xFE) code = (short)(0xFE00 | il[pos++]);
            OpCode op;
            if (!Ops.TryGetValue(code, out op)) { lines.Add("?" + code); continue; }
            string name = op.Name, arg = "";
            switch (op.OperandType)
            {
                case OperandType.InlineNone: break;
                case OperandType.ShortInlineBrTarget: { int t = (sbyte)il[pos]; pos += 1; branches.Add(new KeyValuePair<int, int>(lines.Count, pos + t)); arg = "@"; break; }
                case OperandType.InlineBrTarget: { int t = BitConverter.ToInt32(il, pos); pos += 4; branches.Add(new KeyValuePair<int, int>(lines.Count, pos + t)); arg = "@"; break; }
                case OperandType.ShortInlineI: arg = ((sbyte)il[pos]).ToString(); pos += 1; break;
                case OperandType.ShortInlineVar:
                    { int v = il[pos]; pos += 1; arg = name.Contains("loc") ? loc(v) : "A" + v; break; }
                case OperandType.InlineVar:
                    { int v = BitConverter.ToUInt16(il, pos); pos += 2; arg = name.Contains("loc") ? loc(v) : "A" + v; break; }
                case OperandType.InlineI: arg = BitConverter.ToInt32(il, pos).ToString(); pos += 4; break;
                case OperandType.InlineI8: arg = BitConverter.ToInt64(il, pos).ToString(); pos += 8; break;
                case OperandType.ShortInlineR: arg = BitConverter.ToSingle(il, pos).ToString("R"); pos += 4; break;
                case OperandType.InlineR: arg = BitConverter.ToDouble(il, pos).ToString("R"); pos += 8; break;
                case OperandType.InlineSwitch:
                    {
                        int n = BitConverter.ToInt32(il, pos); pos += 4;
                        int baseAt = pos + n * 4;
                        for (int i = 0; i < n; i++) branches.Add(new KeyValuePair<int, int>(lines.Count, baseAt + BitConverter.ToInt32(il, pos + i * 4)));
                        pos += n * 4; arg = "switch" + n; break;
                    }
                case OperandType.InlineString: arg = "\"" + module.ResolveString(BitConverter.ToInt32(il, pos)) + "\""; pos += 4; break;
                case OperandType.InlineSig: arg = "sig"; pos += 4; break;
                default:
                    {
                        int tok = BitConverter.ToInt32(il, pos); pos += 4;
                        MemberInfo mi = null;
                        try { mi = module.ResolveMember(tok, targs, margs); } catch { }
                        arg = Describe(mi);
                        // Lambdas: se compara también su código.
                        var target = mi as MethodBase;
                        if (target != null && (target.Name.Contains("<") || Generated(target.DeclaringType)) && target.Module == module && (name == "ldftn" || name.StartsWith("call") || name == "newobj"))
                            arg += " {" + Body(target, seen).Replace("\n", " ; ") + "}";
                        break;
                    }
            }
            // Formas cortas y largas de la misma instrucción (stloc.0 = stloc.s 0, br.s = br…).
            var mm = Regex.Match(name, @"^(ldloc|stloc)\.(\d)$");
            if (mm.Success) { arg = loc(int.Parse(mm.Groups[2].Value)); name = mm.Groups[1].Value; }
            mm = Regex.Match(name, @"^ldarg\.(\d)$");
            if (mm.Success) { arg = "A" + mm.Groups[1].Value; name = "ldarg"; }
            mm = Regex.Match(name, @"^ldc\.i4\.(m1|\d)$");
            if (mm.Success) { arg = mm.Groups[1].Value == "m1" ? "-1" : mm.Groups[1].Value; name = "ldc.i4"; }
            if (name.EndsWith(".s")) name = name.Substring(0, name.Length - 2);
            lines.Add(name + (arg.Length > 0 ? " " + arg : ""));
        }
        foreach (var br in branches)
        {
            int idx = starts.IndexOf(br.Value);
            lines[br.Key] = lines[br.Key] + "→" + idx;
        }
        var sb = new StringBuilder();
        foreach (var c in body.ExceptionHandlingClauses)
            sb.Append("try[" + starts.IndexOf(c.TryOffset) + "+" + c.TryLength + "] " + c.Flags + (c.Flags == ExceptionHandlingClauseOptions.Clause ? " " + c.CatchType.FullName : "") + "\n");
        return sb + string.Join("\n", lines);
    }

    static string Describe(MemberInfo mi)
    {
        if (mi == null) return "?";
        var t = mi as Type;
        if (t != null) return Norm(t.FullName ?? t.Name);
        var mb = mi as MethodBase;
        string owner = mi.DeclaringType != null ? Norm(mi.DeclaringType.FullName ?? mi.DeclaringType.Name) : "";
        if (mb != null) return owner + "::" + Norm(mb.Name) + "(" + string.Join(",", mb.GetParameters().Select(p => Norm(p.ParameterType.FullName ?? p.ParameterType.Name))) + ")";
        return owner + "::" + Norm(mi.Name);
    }
}
