using System.Security.Cryptography;
using System.Text;
using YasnNative.Config;

namespace YasnNative.Core;

public sealed class ModuleConfig
{
    public string? Root { get; init; }

    public List<string> Paths { get; init; } = [];
}

public sealed class ResolvedModule
{
    public required string Path { get; init; }

    public required ProgramNode Program { get; init; }

    public required Dictionary<string, Stmt> Exports { get; init; }

    public required string Tag { get; init; }
}

public sealed class ModuleResolver
{
    private readonly Dictionary<string, ResolvedModule> _resolved = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _resolvingStack = [];
    private readonly Dictionary<string, string> _tags = new(StringComparer.OrdinalIgnoreCase);

    private string? _projectRoot;
    private string? _depsRoot;
    private ModuleConfig _config = new();

    public ProgramNode ResolveEntry(string source, string? entryPath)
    {
        var entry = entryPath is null
            ? System.IO.Path.Combine(Directory.GetCurrentDirectory(), "<stdin>")
            : System.IO.Path.GetFullPath(entryPath);

        InitProjectContext(entry);

        var entryProgram = new Parser(Lexer.Tokenize(source, entry), entry).Parse();
        var resolved = ResolveModule(entry, entryProgram, isEntry: true);
        return resolved.Program;
    }

    private void InitProjectContext(string entryPath)
    {
        var baseDir = Directory.Exists(entryPath)
            ? System.IO.Path.GetFullPath(entryPath)
            : System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(entryPath)) ?? Directory.GetCurrentDirectory();

        var current = new DirectoryInfo(baseDir);
        string? foundProject = null;
        string? foundConfig = null;

        while (current is not null)
        {
            var yasnToml = System.IO.Path.Combine(current.FullName, "yasn.toml");
            if (File.Exists(yasnToml))
            {
                foundProject = current.FullName;
                foundConfig = yasnToml;
                break;
            }

            current = current.Parent;
        }

        _projectRoot = foundProject;
        _depsRoot = foundProject is null ? null : System.IO.Path.Combine(foundProject, ".yasn", "deps");
        _config = foundConfig is null ? new ModuleConfig() : LoadModuleConfig(foundConfig);
    }

    private static ModuleConfig LoadModuleConfig(string configPath)
    {
        var data = TomlUtil.ReadToml(configPath);
        var modules = TomlUtil.GetTable(data, "modules", configPath);
        return new ModuleConfig
        {
            Root = TomlUtil.GetString(modules, "root", configPath),
            Paths = TomlUtil.GetStringList(modules, "paths", configPath),
        };
    }

    private ResolvedModule ResolveModule(string modulePath, ProgramNode? program, bool isEntry)
    {
        var normalized = System.IO.Path.GetFullPath(modulePath);
        if (_resolved.TryGetValue(normalized, out var cached))
        {
            return cached;
        }

        if (_resolvingStack.Contains(normalized, StringComparer.OrdinalIgnoreCase))
        {
            var chain = string.Join(" -> ", _resolvingStack.Concat([normalized]));
            throw new YasnException($"Обнаружен циклический импорт: {chain}", path: normalized);
        }

        _resolvingStack.Add(normalized);
        try
        {
            ProgramNode parsed;
            if (program is null)
            {
                var source = File.ReadAllText(normalized);
                parsed = new Parser(Lexer.Tokenize(source, normalized), normalized).Parse();
            }
            else
            {
                parsed = program;
            }

            var linkedStatements = LinkStatements(parsed.Statements, normalized, isEntry);
            var linkedProgram = new ProgramNode(parsed.Line, parsed.Col, linkedStatements);
            var exports = CollectExports(linkedStatements);

            var resolved = new ResolvedModule
            {
                Path = normalized,
                Program = linkedProgram,
                Exports = exports,
                Tag = ModuleTag(normalized),
            };
            _resolved[normalized] = resolved;
            return resolved;
        }
        finally
        {
            _resolvingStack.RemoveAt(_resolvingStack.Count - 1);
        }
    }

    private Dictionary<string, Stmt> CollectExports(List<Stmt> statements)
    {
        var decls = statements
            .Where(static s => s is VarDeclStmt || s is FuncDeclStmt)
            .ToList();

        var explicitExports = decls.Any(static s => s switch
        {
            VarDeclStmt vd => vd.Exported,
            FuncDeclStmt fd => fd.Exported,
            _ => false,
        });

        var exports = new Dictionary<string, Stmt>(StringComparer.Ordinal);
        foreach (var stmt in decls)
        {
            var name = DeclName(stmt);
            if (name is null || name == "main" || name.StartsWith("__мод_", StringComparison.Ordinal))
            {
                continue;
            }

            var exported = stmt switch
            {
                VarDeclStmt vd => vd.Exported,
                FuncDeclStmt fd => fd.Exported,
                _ => false,
            };

            if (explicitExports && !exported)
            {
                continue;
            }

            exports[name] = stmt;
        }

        return exports;
    }

    private List<Stmt> LinkStatements(List<Stmt> statements, string modulePath, bool isEntry)
    {
        var linked = new List<Stmt>();
        var topDeclNames = new HashSet<string>(StringComparer.Ordinal);
        var importNameMap = new Dictionary<string, string>(StringComparer.Ordinal);
        var namespaceMap = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
        var nonImportSeen = false;

        foreach (var stmt in statements)
        {
            if (stmt is ImportAllStmt or ImportFromStmt)
            {
                if (nonImportSeen)
                {
                    throw new YasnException(
                        "Операторы 'подключить/из ... подключить' должны идти до остальных объявлений",
                        stmt.Line,
                        stmt.Col,
                        modulePath);
                }

                var imported = ResolveImport(stmt, modulePath, importNameMap, namespaceMap, topDeclNames);
                linked.AddRange(imported);
                continue;
            }

            nonImportSeen = true;

            if (!isEntry && stmt is not VarDeclStmt and not FuncDeclStmt)
            {
                throw new YasnException(
                    "В подключаемом модуле разрешены только объявления и вложенные блоки внутри функций",
                    stmt.Line,
                    stmt.Col,
                    modulePath);
            }

            var declName = DeclName(stmt);
            if (declName is not null)
            {
                if (importNameMap.ContainsKey(declName))
                {
                    throw new YasnException($"Конфликт имён: '{declName}' уже импортировано", stmt.Line, stmt.Col, modulePath);
                }

                if (namespaceMap.ContainsKey(declName))
                {
                    throw new YasnException($"Конфликт имён: '{declName}' уже занято как namespace", stmt.Line, stmt.Col, modulePath);
                }
            }

            var rewritten = new AliasRewriter(importNameMap, namespaceMap).RewriteStmt(stmt);
            AppendDeclWithConflictCheck(linked, topDeclNames, rewritten, stmt.Line, stmt.Col, modulePath);
        }

        return linked;
    }

    private List<Stmt> ResolveImport(
        Stmt stmt,
        string currentModule,
        Dictionary<string, string> importNameMap,
        Dictionary<string, Dictionary<string, string>> namespaceMap,
        HashSet<string> topDeclNames)
    {
        return stmt switch
        {
            ImportAllStmt importAll => ResolveImportAll(importAll, currentModule, importNameMap, namespaceMap, topDeclNames),
            ImportFromStmt importFrom => ResolveImportFrom(importFrom, currentModule, importNameMap, topDeclNames),
            _ => [],
        };
    }
    private List<Stmt> ResolveImportAll(
        ImportAllStmt stmt,
        string currentModule,
        Dictionary<string, string> importNameMap,
        Dictionary<string, Dictionary<string, string>> namespaceMap,
        HashSet<string> topDeclNames)
    {
        var target = ResolveModulePath(stmt.ModulePath, currentModule, stmt.Line, stmt.Col);
        var resolved = ResolveModule(target, program: null, isEntry: false);
        var exportedNames = resolved.Exports.Keys.ToList();

        var includeSet = ExpandWithDependencies(resolved, exportedNames);
        var (materialized, exposeMap) = MaterializeImportedDecls(resolved, includeSet.ToList(), exportedNames);
        var onlyNew = OnlyNewImported(materialized, topDeclNames);
        AppendImported(onlyNew, topDeclNames, stmt, currentModule);

        if (stmt.Alias is not null)
        {
            var alias = stmt.Alias;
            if (namespaceMap.ContainsKey(alias) || importNameMap.ContainsKey(alias) || topDeclNames.Contains(alias))
            {
                throw new YasnException($"Конфликт имени пространства модуля: '{alias}'", stmt.Line, stmt.Col, currentModule);
            }

            namespaceMap[alias] = exposeMap;
            return onlyNew;
        }

        foreach (var (exportedName, uniqueName) in exposeMap)
        {
            if (importNameMap.ContainsKey(exportedName) || topDeclNames.Contains(exportedName))
            {
                throw new YasnException($"Конфликт имени при подключении: '{exportedName}' уже объявлено", stmt.Line, stmt.Col, currentModule);
            }

            importNameMap[exportedName] = uniqueName;
        }

        return onlyNew;
    }

    private List<Stmt> ResolveImportFrom(
        ImportFromStmt stmt,
        string currentModule,
        Dictionary<string, string> importNameMap,
        HashSet<string> topDeclNames)
    {
        var target = ResolveModulePath(stmt.ModulePath, currentModule, stmt.Line, stmt.Col);
        var resolved = ResolveModule(target, program: null, isEntry: false);

        var requested = new List<string>();
        foreach (var item in stmt.Items)
        {
            if (!resolved.Exports.ContainsKey(item.Name))
            {
                throw new YasnException($"Символ '{item.Name}' не найден в модуле '{target}'", stmt.Line, stmt.Col, currentModule);
            }

            if (!requested.Contains(item.Name, StringComparer.Ordinal))
            {
                requested.Add(item.Name);
            }
        }

        var includeSet = ExpandWithDependencies(resolved, requested);
        var (materialized, exposeMap) = MaterializeImportedDecls(resolved, includeSet.ToList(), requested);
        var onlyNew = OnlyNewImported(materialized, topDeclNames);
        AppendImported(onlyNew, topDeclNames, stmt, currentModule);

        var seenLocal = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in stmt.Items)
        {
            var localName = item.Alias ?? item.Name;
            if (!seenLocal.Add(localName))
            {
                continue;
            }

            if (importNameMap.ContainsKey(localName) || topDeclNames.Contains(localName))
            {
                throw new YasnException($"Конфликт имени при подключении: '{localName}' уже объявлено", item.Line, item.Col, currentModule);
            }

            importNameMap[localName] = exposeMap[item.Name];
        }

        return onlyNew;
    }

    private HashSet<string> ExpandWithDependencies(ResolvedModule resolved, List<string> roots)
    {
        var includeSet = new HashSet<string>(StringComparer.Ordinal);
        var queue = new Queue<string>(roots);

        var declarations = new Dictionary<string, Stmt>(StringComparer.Ordinal);
        foreach (var stmt in resolved.Program.Statements)
        {
            var name = DeclName(stmt);
            if (name is not null)
            {
                declarations[name] = stmt;
            }
        }

        var availableNames = declarations.Keys.ToHashSet(StringComparer.Ordinal);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (includeSet.Contains(current) || !declarations.ContainsKey(current))
            {
                continue;
            }

            includeSet.Add(current);
            var deps = DirectDependencies(declarations[current], availableNames);
            foreach (var dep in deps)
            {
                if (!includeSet.Contains(dep))
                {
                    queue.Enqueue(dep);
                }
            }
        }

        return includeSet;
    }

    private (List<Stmt> Materialized, Dictionary<string, string> ExposeMap) MaterializeImportedDecls(
        ResolvedModule resolved,
        List<string> names,
        List<string>? exposedNames = null)
    {
        var declarations = new Dictionary<string, Stmt>(StringComparer.Ordinal);
        foreach (var stmt in resolved.Program.Statements)
        {
            var name = DeclName(stmt);
            if (name is not null)
            {
                declarations[name] = stmt;
            }
        }

        var selected = names.Where(declarations.ContainsKey).ToHashSet(StringComparer.Ordinal);
        var exposed = (exposedNames ?? names).ToHashSet(StringComparer.Ordinal);

        var renameMap = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var name in selected)
        {
            renameMap[name] = UniqueSymbolName(resolved, name);
        }

        var materialized = new List<Stmt>();
        var renamer = new RenameSymbols(renameMap);

        foreach (var stmt in resolved.Program.Statements)
        {
            var name = DeclName(stmt);
            if (name is null || !selected.Contains(name))
            {
                continue;
            }

            var renamed = renamer.RewriteStmt(stmt);
            renamed = renamed switch
            {
                VarDeclStmt vd => vd with { Exported = false },
                FuncDeclStmt fd => fd with { Exported = false },
                _ => renamed,
            };
            materialized.Add(renamed);
        }

        var exposeMap = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var name in exposed)
        {
            if (renameMap.TryGetValue(name, out var renamed))
            {
                exposeMap[name] = renamed;
            }
        }

        return (materialized, exposeMap);
    }

    private static List<Stmt> OnlyNewImported(List<Stmt> imported, HashSet<string> topDeclNames)
    {
        var result = new List<Stmt>();
        foreach (var stmt in imported)
        {
            var name = DeclName(stmt);
            if (name is not null && topDeclNames.Contains(name))
            {
                continue;
            }

            result.Add(stmt);
        }

        return result;
    }

    private static void AppendImported(
        List<Stmt> imported,
        HashSet<string> topDeclNames,
        Stmt importStmt,
        string currentModule)
    {
        foreach (var stmt in imported)
        {
            var name = DeclName(stmt);
            if (name is null)
            {
                continue;
            }

            if (topDeclNames.Contains(name))
            {
                continue;
            }

            topDeclNames.Add(name);
        }
    }

    private static void AppendDeclWithConflictCheck(
        List<Stmt> linked,
        HashSet<string> namesInScope,
        Stmt stmt,
        int sourceLine,
        int sourceCol,
        string sourcePath)
    {
        var name = DeclName(stmt);
        if (name is not null)
        {
            if (namesInScope.Contains(name))
            {
                throw new YasnException($"Конфликт имён: '{name}' уже объявлено", sourceLine, sourceCol, sourcePath);
            }

            namesInScope.Add(name);
        }

        linked.Add(stmt);
    }

    private string ResolveModulePath(string rawPath, string currentModule, int line, int col)
    {
        var raw = rawPath.Trim();
        var variants = new List<string>();
        if (System.IO.Path.HasExtension(raw))
        {
            variants.Add(raw);
        }
        else
        {
            variants.Add(raw + ".яс");
        }

        var candidates = new List<string>();
        foreach (var variant in variants)
        {
            if (System.IO.Path.IsPathRooted(variant))
            {
                candidates.Add(System.IO.Path.GetFullPath(variant));
                continue;
            }

            var currentDir = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(currentModule)) ?? Directory.GetCurrentDirectory();
            candidates.Add(System.IO.Path.GetFullPath(System.IO.Path.Combine(currentDir, variant)));

            if (_projectRoot is not null)
            {
                if (!string.IsNullOrWhiteSpace(_config.Root))
                {
                    candidates.Add(System.IO.Path.GetFullPath(System.IO.Path.Combine(_projectRoot, _config.Root!, variant)));
                }

                foreach (var path in _config.Paths)
                {
                    candidates.Add(System.IO.Path.GetFullPath(System.IO.Path.Combine(_projectRoot, path, variant)));
                }
            }

            if (_depsRoot is not null && Directory.Exists(_depsRoot))
            {
                candidates.Add(System.IO.Path.GetFullPath(System.IO.Path.Combine(_depsRoot, variant)));

                var depName = variant.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
                if (!string.IsNullOrWhiteSpace(depName))
                {
                    candidates.Add(System.IO.Path.GetFullPath(System.IO.Path.Combine(_depsRoot, depName, variant)));
                }

                foreach (var depDir in Directory.EnumerateDirectories(_depsRoot))
                {
                    candidates.Add(System.IO.Path.GetFullPath(System.IO.Path.Combine(depDir, variant)));
                }
            }
        }

        foreach (var candidate in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new YasnException($"Не удалось найти модуль: {rawPath}", line, col, currentModule);
    }
    private static string? DeclName(Stmt stmt)
    {
        return stmt switch
        {
            VarDeclStmt vd => vd.Name,
            FuncDeclStmt fd => fd.Name,
            _ => null,
        };
    }

    private string ModuleTag(string modulePath)
    {
        if (_tags.TryGetValue(modulePath, out var tag))
        {
            return tag;
        }

        var bytes = SHA1.HashData(Encoding.UTF8.GetBytes(modulePath));
        var suffix = Convert.ToHexString(bytes)[..8].ToLowerInvariant();
        tag = "__мод_" + suffix;
        _tags[modulePath] = tag;
        return tag;
    }

    private string UniqueSymbolName(ResolvedModule module, string name)
    {
        return $"{module.Tag}_{name}";
    }

    private static HashSet<string> DirectDependencies(Stmt stmt, HashSet<string> topNames)
    {
        var deps = new HashSet<string>(StringComparer.Ordinal);
        var locals = new HashSet<string>(StringComparer.Ordinal);

        void VisitStmt(Stmt s)
        {
            switch (s)
            {
                case VarDeclStmt vd:
                    VisitExpr(vd.Value);
                    locals.Add(vd.Name);
                    break;
                case AssignStmt assign:
                    VisitExpr(assign.Value);
                    break;
                case IndexAssignStmt idx:
                    VisitExpr(idx.Target);
                    VisitExpr(idx.Index);
                    VisitExpr(idx.Value);
                    break;
                case ExprStmt es:
                    VisitExpr(es.Expr);
                    break;
                case ReturnStmt rs:
                    VisitExpr(rs.Value);
                    break;
                case IfStmt ifs:
                    VisitExpr(ifs.Condition);
                    foreach (var inner in ifs.ThenBody)
                    {
                        VisitStmt(inner);
                    }
                    if (ifs.ElseBody is not null)
                    {
                        foreach (var inner in ifs.ElseBody)
                        {
                            VisitStmt(inner);
                        }
                    }
                    break;
                case WhileStmt ws:
                    VisitExpr(ws.Condition);
                    foreach (var inner in ws.Body)
                    {
                        VisitStmt(inner);
                    }
                    break;
                case ForStmt fs:
                    VisitExpr(fs.Iterable);
                    locals.Add(fs.VarName);
                    foreach (var inner in fs.Body)
                    {
                        VisitStmt(inner);
                    }
                    break;
                case FuncDeclStmt fd:
                    foreach (var p in fd.Params)
                    {
                        locals.Add(p.Name);
                    }
                    foreach (var inner in fd.Body)
                    {
                        VisitStmt(inner);
                    }
                    break;
            }
        }

        void VisitExpr(Expr expr)
        {
            switch (expr)
            {
                case IdentifierExpr id:
                    if (!locals.Contains(id.Name) && topNames.Contains(id.Name))
                    {
                        deps.Add(id.Name);
                    }
                    break;
                case UnaryExpr u:
                    VisitExpr(u.Operand);
                    break;
                case AwaitExpr a:
                    VisitExpr(a.Operand);
                    break;
                case BinaryExpr b:
                    VisitExpr(b.Left);
                    VisitExpr(b.Right);
                    break;
                case CallExpr c:
                    VisitExpr(c.Callee);
                    foreach (var arg in c.Args)
                    {
                        VisitExpr(arg);
                    }
                    break;
                case ListLiteralExpr l:
                    foreach (var item in l.Elements)
                    {
                        VisitExpr(item);
                    }
                    break;
                case DictLiteralExpr d:
                    foreach (var (k, v) in d.Entries)
                    {
                        VisitExpr(k);
                        VisitExpr(v);
                    }
                    break;
                case IndexExpr i:
                    VisitExpr(i.Target);
                    VisitExpr(i.Index);
                    break;
                case MemberExpr m:
                    VisitExpr(m.Target);
                    break;
            }
        }

        VisitStmt(stmt);
        return deps;
    }
}

internal sealed class AliasRewriter
{
    private readonly Dictionary<string, string> _importNameMap;
    private readonly Dictionary<string, Dictionary<string, string>> _namespaceMap;

    public AliasRewriter(
        Dictionary<string, string> importNameMap,
        Dictionary<string, Dictionary<string, string>> namespaceMap)
    {
        _importNameMap = importNameMap;
        _namespaceMap = namespaceMap;
    }

    public Stmt RewriteStmt(Stmt stmt)
    {
        return stmt switch
        {
            VarDeclStmt vd => vd with { Value = RewriteExpr(vd.Value) },
            AssignStmt assign => assign with { Value = RewriteExpr(assign.Value) },
            IndexAssignStmt idx => idx with
            {
                Target = RewriteExpr(idx.Target),
                Index = RewriteExpr(idx.Index),
                Value = RewriteExpr(idx.Value),
            },
            ExprStmt es => es with { Expr = RewriteExpr(es.Expr) },
            ReturnStmt rs => rs with { Value = RewriteExpr(rs.Value) },
            IfStmt ifs => ifs with
            {
                Condition = RewriteExpr(ifs.Condition),
                ThenBody = ifs.ThenBody.Select(RewriteStmt).ToList(),
                ElseBody = ifs.ElseBody?.Select(RewriteStmt).ToList(),
            },
            WhileStmt ws => ws with
            {
                Condition = RewriteExpr(ws.Condition),
                Body = ws.Body.Select(RewriteStmt).ToList(),
            },
            ForStmt fs => fs with
            {
                Iterable = RewriteExpr(fs.Iterable),
                Body = fs.Body.Select(RewriteStmt).ToList(),
            },
            FuncDeclStmt fd => fd with
            {
                Body = fd.Body.Select(RewriteStmt).ToList(),
            },
            _ => stmt,
        };
    }

    public Expr RewriteExpr(Expr expr)
    {
        switch (expr)
        {
            case IdentifierExpr id:
                return _importNameMap.TryGetValue(id.Name, out var mapped)
                    ? new IdentifierExpr(id.Line, id.Col, mapped)
                    : expr;

            case MemberExpr member:
                if (member.Target is IdentifierExpr namespaceIdent
                    && _namespaceMap.TryGetValue(namespaceIdent.Name, out var nsMap)
                    && nsMap.TryGetValue(member.Member, out var symbolName))
                {
                    return new IdentifierExpr(member.Line, member.Col, symbolName);
                }
                return member with { Target = RewriteExpr(member.Target) };

            case UnaryExpr unary:
                return unary with { Operand = RewriteExpr(unary.Operand) };

            case AwaitExpr awaitExpr:
                return awaitExpr with { Operand = RewriteExpr(awaitExpr.Operand) };

            case BinaryExpr binary:
                return binary with { Left = RewriteExpr(binary.Left), Right = RewriteExpr(binary.Right) };

            case CallExpr call:
                return call with
                {
                    Callee = RewriteExpr(call.Callee),
                    Args = call.Args.Select(RewriteExpr).ToList(),
                };

            case ListLiteralExpr list:
                return list with { Elements = list.Elements.Select(RewriteExpr).ToList() };

            case DictLiteralExpr dict:
                return dict with
                {
                    Entries = dict.Entries.Select(pair => (RewriteExpr(pair.Key), RewriteExpr(pair.Value))).ToList(),
                };

            case IndexExpr idx:
                return idx with { Target = RewriteExpr(idx.Target), Index = RewriteExpr(idx.Index) };

            default:
                return expr;
        }
    }
}

internal sealed class RenameSymbols
{
    private readonly Dictionary<string, string> _renameMap;

    public RenameSymbols(Dictionary<string, string> renameMap)
    {
        _renameMap = renameMap;
    }

    public Stmt RewriteStmt(Stmt stmt)
    {
        return stmt switch
        {
            VarDeclStmt vd => vd with { Name = Rename(vd.Name), Value = RewriteExpr(vd.Value) },
            AssignStmt assign => assign with { Name = Rename(assign.Name), Value = RewriteExpr(assign.Value) },
            IndexAssignStmt idx => idx with
            {
                Target = RewriteExpr(idx.Target),
                Index = RewriteExpr(idx.Index),
                Value = RewriteExpr(idx.Value),
            },
            ExprStmt es => es with { Expr = RewriteExpr(es.Expr) },
            ReturnStmt rs => rs with { Value = RewriteExpr(rs.Value) },
            IfStmt ifs => ifs with
            {
                Condition = RewriteExpr(ifs.Condition),
                ThenBody = ifs.ThenBody.Select(RewriteStmt).ToList(),
                ElseBody = ifs.ElseBody?.Select(RewriteStmt).ToList(),
            },
            WhileStmt ws => ws with
            {
                Condition = RewriteExpr(ws.Condition),
                Body = ws.Body.Select(RewriteStmt).ToList(),
            },
            ForStmt fs => fs with
            {
                VarName = Rename(fs.VarName),
                Iterable = RewriteExpr(fs.Iterable),
                Body = fs.Body.Select(RewriteStmt).ToList(),
            },
            FuncDeclStmt fd => fd with
            {
                Name = Rename(fd.Name),
                Params = fd.Params.Select(p => p with { Name = Rename(p.Name) }).ToList(),
                Body = fd.Body.Select(RewriteStmt).ToList(),
            },
            _ => stmt,
        };
    }

    public Expr RewriteExpr(Expr expr)
    {
        return expr switch
        {
            IdentifierExpr id => id with { Name = Rename(id.Name) },
            MemberExpr member => member with { Target = RewriteExpr(member.Target) },
            UnaryExpr unary => unary with { Operand = RewriteExpr(unary.Operand) },
            AwaitExpr awaitExpr => awaitExpr with { Operand = RewriteExpr(awaitExpr.Operand) },
            BinaryExpr binary => binary with { Left = RewriteExpr(binary.Left), Right = RewriteExpr(binary.Right) },
            CallExpr call => call with { Callee = RewriteExpr(call.Callee), Args = call.Args.Select(RewriteExpr).ToList() },
            ListLiteralExpr list => list with { Elements = list.Elements.Select(RewriteExpr).ToList() },
            DictLiteralExpr dict => dict with { Entries = dict.Entries.Select(pair => (RewriteExpr(pair.Key), RewriteExpr(pair.Value))).ToList() },
            IndexExpr idx => idx with { Target = RewriteExpr(idx.Target), Index = RewriteExpr(idx.Index) },
            _ => expr,
        };
    }

    private string Rename(string name)
    {
        return _renameMap.TryGetValue(name, out var mapped) ? mapped : name;
    }
}
