from __future__ import annotations

from .bc import ProgramBC
from .checker import CheckResult, TypeChecker
from .compiler import CompileResult, Compiler
from .lexer import tokenize
from .module_loader import resolve_modules
from .parser import Parser
from .vm import VirtualMachine


def parse_source(source: str, path: str | None = None):
    tokens = tokenize(source, path=path)
    parser = Parser(tokens=tokens, path=path)
    return parser.parse()


def check_program(program, path: str | None = None) -> CheckResult:
    checker = TypeChecker(path=path)
    return checker.check(program)


def compile_program(program, path: str | None = None) -> CompileResult:
    compiler = Compiler(path=path)
    return compiler.compile(program)


def compile_source(source: str, path: str | None = None) -> ProgramBC:
    program = resolve_modules(source, path=path)
    check_program(program, path=path)
    compiled = compile_program(program, path=path)
    return compiled.program


def load_program(source: str, path: str | None = None):
    return resolve_modules(source, path=path)


def run_program(program: ProgramBC, path: str | None = None) -> None:
    vm = VirtualMachine(program=program, path=path)
    vm.run()
