// Compara dos compilaciones de StreamDelay instrucción por instrucción (IL), clase por clase.
// Los nombres de variables locales no existen en el IL, así que renombrar variables o añadir
// comentarios no cambia nada; cualquier cambio de lógica sí aparece como diferencia.
// Tampoco cuentan: los números que pone el compilador a lambdas y clases internas, el nombre de las
// variables que captura una lambda, el número de cada variable local, las formas cortas/largas de
// una instrucción y la copia de más que hace "T[] a = campo; foreach (… in a)" (ver Body).
// Uso: ildiff.exe A.exe B.exe [Clase1,Clase2,...]
//   ILDIFF_TODO=1          lista todos los métodos distintos (no solo 8 por clase)
//   ILDIFF_VOLCAR=carpeta  guarda el código de cada método distinto en carpeta\A y carpeta\B
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
                // ILDIFF_VOLCAR=carpeta: guarda las dos versiones de cada método distinto para revisarlas con un diff.
                string dump = Environment.GetEnvironmentVariable("ILDIFF_VOLCAR");
                if (x != y && dump != null)
                {
                    string file = Regex.Replace(name.Replace("StreamDelay.", "") + "." + k.Split('(')[0], @"[^\w.+-]", "_");
                    Directory.CreateDirectory(Path.Combine(dump, "A")); Directory.CreateDirectory(Path.Combine(dump, "B"));
                    File.WriteAllText(Path.Combine(dump, "A", file + ".txt"), (x ?? "").Replace(" ; ", "\n    "));
                    File.WriteAllText(Path.Combine(dump, "B", file + ".txt"), (y ?? "").Replace(" ; ", "\n    "));
                }
            }
            if (bad.Count == 0) { Console.WriteLine("IDÉNTICA   " + name + "  (" + ma.Count + " miembros)"); same++; }
            else { Console.WriteLine("DIFERENTE  " + name + "  (" + bad.Count + " de " + Math.Max(ma.Count, mb.Count) + ")"); foreach (var b in bad.Take(Environment.GetEnvironmentVariable("ILDIFF_TODO") != null ? 1000 : 8)) Console.WriteLine("           · " + b); diff++; }
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
        s = Regex.Replace(s, @"(CachedAnonymousMethodDelegate|DisplayClass|>b__|>c__|__locals)[0-9a-f]+(_[0-9a-f]+)?", "$1#");
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
        var fieldMap = new Dictionary<string, int>();
        // Las variables locales se escriben primero como §n§ y se numeran al final (ver más abajo).
        Func<int, string> loc = i => "§" + i + "§";
        Func<int, Type> locType = i => i < body.LocalVariables.Count ? body.LocalVariables[i].LocalType : null;
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
                        // Variables capturadas por una lambda: el compilador las guarda como campos con el
                        // nombre de la variable. Renombrar la variable no cambia nada: se numeran por orden.
                        if (mi is FieldInfo && Generated(mi.DeclaringType))
                        {
                            int v;
                            string fk = mi.DeclaringType.FullName + "::" + mi.Name;
                            if (!fieldMap.TryGetValue(fk, out v)) { v = fieldMap.Count; fieldMap[fk] = v; }
                            arg = Norm(mi.DeclaringType.FullName) + "::F" + v + ":" + Norm(((FieldInfo)mi).FieldType.FullName);
                        }
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
        // "foreach (var x in array)" sobre una copia de la tabla: el código descompilado hace
        // "T[] a = campo; foreach (var x in a)", que añade una o dos copias más (ldloc a; stloc tmp)
        // justo antes del bucle (ldc.i4 0; stloc i; br). Recorrer la misma tabla con una variable más
        // o menos no cambia nada: se quita la copia y la variable copiada se trata como la original.
        // Solo si ninguna de las dos variables se vuelve a escribir (ni se pasa por referencia):
        // así es seguro que siempre tienen la misma tabla.
        var drop = new bool[lines.Count];
        var alias = new Dictionary<int, int>();
        var locRx = new Regex(@"^(ldloc|stloc|ldloca) §(\d+)§$");
        Func<int, int> next = k => { k++; while (k < lines.Count && drop[k]) k++; return k; };
        Func<int, int, int> writes = (v, skip) =>
        {
            int count = 0;
            for (int k = 0; k < lines.Count; k++)
            {
                if (k == skip) continue;
                Match w = locRx.Match(lines[k]);
                if (w.Success && w.Groups[1].Value != "ldloc" && int.Parse(w.Groups[2].Value) == v) count++;
            }
            return count;
        };
        for (int i = lines.Count - 1; i >= 0; i--)
        {
            int i1 = next(i), i2 = next(i1), i3 = next(i2), i4 = next(i3);
            if (i4 >= lines.Count) continue;
            Match a1 = locRx.Match(lines[i]), a2 = locRx.Match(lines[i1]), a4 = locRx.Match(lines[i3]);
            if (!a1.Success || a1.Groups[1].Value != "ldloc" || !a2.Success || a2.Groups[1].Value != "stloc") continue;
            if (lines[i2] != "ldc.i4 0" || !a4.Success || a4.Groups[1].Value != "stloc" || !lines[i4].StartsWith("br @")) continue;
            int x = int.Parse(a1.Groups[2].Value), y = int.Parse(a2.Groups[2].Value);
            Type tx = locType(x), ty = locType(y);
            if (tx == null || tx != ty || !tx.IsArray) continue;
            // x se escribe una vez (antes) y y solo aquí.
            if (writes(x, -1) > 1 || writes(y, i1) > 0) continue;
            drop[i] = drop[i1] = true;
            alias[y] = x;
        }
        // Número nuevo de cada instrucción (sin las quitadas).
        var newIndex = new int[lines.Count + 1];
        for (int i = 0, n = 0; i <= lines.Count; i++) { newIndex[i] = n; if (i < lines.Count && !drop[i]) n++; }
        Func<int, int> at = offset => { int k = starts.IndexOf(offset); return k < 0 ? (offset >= il.Length ? newIndex[lines.Count] : -1) : newIndex[k]; };
        foreach (var br in branches)
            lines[br.Key] = lines[br.Key] + "→" + at(br.Value);
        // Variables locales numeradas por orden de aparición (así da igual qué número les puso el compilador).
        var localMap = new Dictionary<int, int>();
        var outLines = new List<string>();
        for (int i = 0; i < lines.Count; i++)
        {
            if (drop[i]) continue;
            outLines.Add(Regex.Replace(lines[i], @"§(\d+)§", m =>
            {
                int raw = int.Parse(m.Groups[1].Value), r;
                while (alias.TryGetValue(raw, out r)) raw = r;
                int v; if (!localMap.TryGetValue(raw, out v)) { v = localMap.Count; localMap[raw] = v; }
                Type lt = locType(raw);
                return "L" + v + ":" + Norm(lt != null ? lt.FullName : "?");
            }));
        }
        var sb = new StringBuilder();
        foreach (var c in body.ExceptionHandlingClauses)
            sb.Append("try[" + at(c.TryOffset) + "-" + at(c.TryOffset + c.TryLength) + "] " + c.Flags + (c.Flags == ExceptionHandlingClauseOptions.Clause ? " " + c.CatchType.FullName : "") + "\n");
        return sb + string.Join("\n", outLines);
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
