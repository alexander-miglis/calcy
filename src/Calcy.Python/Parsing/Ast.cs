using Calcy.Python.Runtime;

namespace Calcy.Python.Parsing;

internal abstract class AstNode
{
    public int Line;
    public int Col;
}

internal abstract class ExprAst : AstNode
{
    public bool Parenthesized;
}

internal abstract class StmtAst : AstNode
{
}

internal enum UnaryKind : byte
{
    Neg,
    Pos,
    Invert,
    Not,
}

internal enum CmpKind : byte
{
    Lt,
    Le,
    Eq,
    Ne,
    Gt,
    Ge,
    In,
    NotIn,
    Is,
    IsNot,
}

internal enum ComprehensionKind : byte
{
    List,
    Set,
    Dict,
    Generator,
}

internal sealed class ConstantAst : ExprAst
{
    public PyObject Value = Py.None;
}

internal sealed class NameAst : ExprAst
{
    public string Id = "";
}

internal sealed class BinOpAst : ExprAst
{
    public BinOp Op;
    public ExprAst Left = null!;
    public ExprAst Right = null!;
}

internal sealed class UnaryOpAst : ExprAst
{
    public UnaryKind Op;
    public ExprAst Operand = null!;
}

internal sealed class BoolOpAst : ExprAst
{
    public bool IsAnd;
    public List<ExprAst> Values = new();
}

internal sealed class CompareAst : ExprAst
{
    public ExprAst Left = null!;
    public List<CmpKind> Ops = new();
    public List<ExprAst> Comparators = new();
}

internal sealed class KeywordAst : AstNode
{
    public string? Name; // null for **kwargs
    public ExprAst Value = null!;
}

internal sealed class CallAst : ExprAst
{
    public ExprAst Func = null!;
    public List<ExprAst> Args = new();
    public List<KeywordAst> Keywords = new();
}

internal sealed class AttributeAst : ExprAst
{
    public ExprAst Value = null!;
    public string Attr = "";
}

internal sealed class SubscriptAst : ExprAst
{
    public ExprAst Value = null!;
    public ExprAst Index = null!;
}

internal sealed class SliceAst : ExprAst
{
    public ExprAst? Lower;
    public ExprAst? Upper;
    public ExprAst? Step;
}

internal sealed class ListAst : ExprAst
{
    public List<ExprAst> Elts = new();
}

internal sealed class TupleAst : ExprAst
{
    public List<ExprAst> Elts = new();
}

internal sealed class SetAst : ExprAst
{
    public List<ExprAst> Elts = new();
}

internal sealed class DictAst : ExprAst
{
    public List<ExprAst?> Keys = new();
    public List<ExprAst> Values = new();
}

internal sealed class ComprehensionFor : AstNode
{
    public ExprAst Target = null!;
    public ExprAst Iter = null!;
    public List<ExprAst> Ifs = new();
}

internal sealed class ComprehensionAst : ExprAst
{
    public ComprehensionKind Kind;
    public ExprAst Elt = null!;
    public ExprAst? Value;
    public List<ComprehensionFor> Generators = new();
}

internal sealed class ArgAst : AstNode
{
    public string Name = "";
    public ExprAst? Annotation;
}

internal sealed class ArgumentsAst
{
    public List<ArgAst> PosOnly = new();
    public List<ArgAst> Args = new();
    public ArgAst? VarArg;
    public List<ArgAst> KwOnly = new();
    public List<ExprAst?> KwDefaults = new();
    public ArgAst? KwArg;
    public List<ExprAst> Defaults = new();

    public IEnumerable<ArgAst> All()
    {
        foreach (var a in PosOnly) yield return a;
        foreach (var a in Args) yield return a;
        foreach (var a in KwOnly) yield return a;
        if (VarArg != null) yield return VarArg;
        if (KwArg != null) yield return KwArg;
    }
}

internal sealed class LambdaAst : ExprAst
{
    public ArgumentsAst Args = new();
    public ExprAst Body = null!;
}

internal sealed class IfExpAst : ExprAst
{
    public ExprAst Test = null!;
    public ExprAst Body = null!;
    public ExprAst OrElse = null!;
}

internal sealed class StarredAst : ExprAst
{
    public ExprAst Value = null!;
}

internal sealed class YieldAst : ExprAst
{
    public ExprAst? Value;
}

internal sealed class YieldFromAst : ExprAst
{
    public ExprAst Value = null!;
}

internal sealed class JoinedStrAst : ExprAst
{
    public List<ExprAst> Parts = new();
}

internal sealed class FormattedValueAst : ExprAst
{
    public ExprAst Value = null!;
    public char Conversion;
    public JoinedStrAst? Spec;
}

internal sealed class NamedExprAst : ExprAst
{
    public NameAst Target = null!;
    public ExprAst Value = null!;
}

// ---------------------------------------------------------------- statements

internal sealed class ModuleAst
{
    public List<StmtAst> Body = new();
}

internal sealed class ExprStmtAst : StmtAst
{
    public ExprAst Value = null!;
}

internal sealed class AssignAst : StmtAst
{
    public List<ExprAst> Targets = new();
    public ExprAst Value = null!;
}

internal sealed class AugAssignAst : StmtAst
{
    public ExprAst Target = null!;
    public BinOp Op;
    public ExprAst Value = null!;
}

internal sealed class AnnAssignAst : StmtAst
{
    public ExprAst Target = null!;
    public ExprAst Annotation = null!;
    public ExprAst? Value;
}

internal sealed class IfAst : StmtAst
{
    public ExprAst Test = null!;
    public List<StmtAst> Body = new();
    public List<StmtAst> OrElse = new();
}

internal sealed class WhileAst : StmtAst
{
    public ExprAst Test = null!;
    public List<StmtAst> Body = new();
    public List<StmtAst> OrElse = new();
}

internal sealed class ForAst : StmtAst
{
    public ExprAst Target = null!;
    public ExprAst Iter = null!;
    public List<StmtAst> Body = new();
    public List<StmtAst> OrElse = new();
}

internal sealed class BreakAst : StmtAst
{
}

internal sealed class ContinueAst : StmtAst
{
}

internal sealed class PassAst : StmtAst
{
}

internal sealed class ReturnAst : StmtAst
{
    public ExprAst? Value;
}

internal sealed class FunctionDefAst : StmtAst
{
    public string Name = "";
    public ArgumentsAst Args = new();
    public List<StmtAst> Body = new();
    public List<ExprAst> Decorators = new();
    public ExprAst? Returns;
}

internal sealed class ClassDefAst : StmtAst
{
    public string Name = "";
    public List<ExprAst> Bases = new();
    public List<KeywordAst> Keywords = new();
    public List<StmtAst> Body = new();
    public List<ExprAst> Decorators = new();
}

internal sealed class GlobalAst : StmtAst
{
    public List<string> Names = new();
}

internal sealed class NonlocalAst : StmtAst
{
    public List<string> Names = new();
}

internal sealed class DeleteAst : StmtAst
{
    public List<ExprAst> Targets = new();
}

internal sealed class HandlerAst : AstNode
{
    public ExprAst? Type;
    public string? Name;
    public List<StmtAst> Body = new();
}

internal sealed class TryAst : StmtAst
{
    public List<StmtAst> Body = new();
    public List<HandlerAst> Handlers = new();
    public List<StmtAst> OrElse = new();
    public List<StmtAst> FinalBody = new();
}

internal sealed class RaiseAst : StmtAst
{
    public ExprAst? Exc;
    public ExprAst? Cause;
}

internal sealed class AssertAst : StmtAst
{
    public ExprAst Test = null!;
    public ExprAst? Msg;
}

internal sealed class ImportAst : StmtAst
{
    public List<(string Name, string? AsName)> Names = new();
}

internal sealed class ImportFromAst : StmtAst
{
    public string Module = "";
    public int Level;
    public List<(string Name, string? AsName)> Names = new();
}

internal sealed class WithAst : StmtAst
{
    public List<(ExprAst Context, ExprAst? Vars)> Items = new();
    public List<StmtAst> Body = new();
}

// ---------------------------------------------------------------- match statement

internal abstract class PatternAst : AstNode
{
}

internal sealed class MatchValuePattern : PatternAst
{
    public ExprAst Value = null!;
}

internal sealed class MatchSingletonPattern : PatternAst
{
    public PyObject Value = Py.None;
}

internal sealed class MatchSequencePattern : PatternAst
{
    public List<PatternAst> Patterns = new();
}

internal sealed class MatchStarPattern : PatternAst
{
    public string? Name;
}

internal sealed class MatchMappingPattern : PatternAst
{
    public List<ExprAst> Keys = new();
    public List<PatternAst> Patterns = new();
    public string? Rest;
}

internal sealed class MatchClassPattern : PatternAst
{
    public ExprAst Cls = null!;
    public List<PatternAst> Patterns = new();
    public List<string> KwdAttrs = new();
    public List<PatternAst> KwdPatterns = new();
}

internal sealed class MatchAsPattern : PatternAst
{
    public PatternAst? Pattern;
    public string? Name;
}

internal sealed class MatchOrPattern : PatternAst
{
    public List<PatternAst> Patterns = new();
}

internal sealed class MatchCaseAst : AstNode
{
    public PatternAst Pattern = null!;
    public ExprAst? Guard;
    public List<StmtAst> Body = new();
}

internal sealed class MatchAst : StmtAst
{
    public ExprAst Subject = null!;
    public List<MatchCaseAst> Cases = new();
}
