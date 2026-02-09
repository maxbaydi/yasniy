from __future__ import annotations

from dataclasses import dataclass
from typing import Callable

from .bc import FunctionBC, ProgramBC
from .diagnostics import YasnyError


BuiltinFn = Callable[[list[object]], object]


@dataclass(slots=True)
class Frame:
    function: FunctionBC
    locals: list[object]
    ip: int = 0


class VirtualMachine:
    def __init__(self, program: ProgramBC, path: str | None = None):
        self.program = program
        self.path = path
        self.globals: list[object] = []
        self._initialized = False
        self.builtins: dict[str, BuiltinFn] = {
            "печать": self._builtin_print,
            "длина": self._builtin_len,
            "диапазон": self._builtin_range,
            "ввод": self._builtin_input,
        }

    def run(self) -> None:
        self.globals = [None] * int(self.program.global_count)
        self._execute_function(self.program.entry, [])
        self._initialized = True

    def call_function(self, function_name: str, args: list[object] | None = None, reset_state: bool = True) -> object:
        call_args = list(args or [])
        if reset_state or not self._initialized:
            self.run()
        if function_name in self.builtins:
            return self.builtins[function_name](call_args)
        if function_name not in self.program.functions:
            raise YasnyError(f"Неизвестная функция: {function_name}", path=self.path)
        return self._execute_function(self.program.functions[function_name], call_args)

    def _execute_function(self, fn: FunctionBC, args: list[object]) -> object:
        if len(args) != len(fn.params):
            raise YasnyError(
                f"Функция '{fn.name}' ожидает {len(fn.params)} аргументов, получено {len(args)}",
                path=self.path,
            )

        frame = Frame(function=fn, locals=[None] * fn.local_count, ip=0)
        for idx, value in enumerate(args):
            frame.locals[idx] = value

        stack: list[object] = []
        instructions = fn.instructions

        while frame.ip < len(instructions):
            ins = instructions[frame.ip]
            frame.ip += 1
            op = ins.op
            argv = ins.args

            if op == "CONST":
                stack.append(argv[0] if argv else None)
            elif op == "CONST_NULL":
                stack.append(None)
            elif op == "LOAD":
                slot = int(argv[0])
                stack.append(frame.locals[slot])
            elif op == "STORE":
                slot = int(argv[0])
                frame.locals[slot] = stack.pop()
            elif op == "GLOAD":
                slot = int(argv[0])
                stack.append(self.globals[slot])
            elif op == "GSTORE":
                slot = int(argv[0])
                self.globals[slot] = stack.pop()
            elif op == "POP":
                if stack:
                    stack.pop()
            elif op == "ADD":
                b, a = self._pop2(stack)
                stack.append(a + b)
            elif op == "SUB":
                b, a = self._pop2(stack)
                stack.append(a - b)
            elif op == "MUL":
                b, a = self._pop2(stack)
                stack.append(a * b)
            elif op == "DIV":
                b, a = self._pop2(stack)
                if isinstance(a, int) and isinstance(b, int):
                    stack.append(int(a / b))
                else:
                    stack.append(a / b)
            elif op == "MOD":
                b, a = self._pop2(stack)
                stack.append(a % b)
            elif op == "NEG":
                stack.append(-stack.pop())
            elif op == "NOT":
                stack.append(not stack.pop())
            elif op == "AND":
                b, a = self._pop2(stack)
                stack.append(bool(a) and bool(b))
            elif op == "OR":
                b, a = self._pop2(stack)
                stack.append(bool(a) or bool(b))
            elif op == "EQ":
                b, a = self._pop2(stack)
                stack.append(a == b)
            elif op == "NE":
                b, a = self._pop2(stack)
                stack.append(a != b)
            elif op == "LT":
                b, a = self._pop2(stack)
                stack.append(a < b)
            elif op == "LE":
                b, a = self._pop2(stack)
                stack.append(a <= b)
            elif op == "GT":
                b, a = self._pop2(stack)
                stack.append(a > b)
            elif op == "GE":
                b, a = self._pop2(stack)
                stack.append(a >= b)
            elif op == "JMP":
                frame.ip = int(argv[0])
            elif op == "JMP_FALSE":
                cond = stack.pop()
                if not bool(cond):
                    frame.ip = int(argv[0])
            elif op == "CALL":
                fn_name = str(argv[0])
                argc = int(argv[1])
                call_args = [stack.pop() for _ in range(argc)]
                call_args.reverse()
                if fn_name in self.builtins:
                    stack.append(self.builtins[fn_name](call_args))
                elif fn_name in self.program.functions:
                    result = self._execute_function(self.program.functions[fn_name], call_args)
                    stack.append(result)
                else:
                    raise YasnyError(f"Неизвестная функция во время выполнения: {fn_name}", path=self.path)
            elif op == "RET":
                return stack.pop() if stack else None
            elif op == "MAKE_LIST":
                count = int(argv[0])
                items = [stack.pop() for _ in range(count)]
                items.reverse()
                stack.append(items)
            elif op == "MAKE_DICT":
                count = int(argv[0])
                raw = [stack.pop() for _ in range(count * 2)]
                raw.reverse()
                obj: dict[object, object] = {}
                for i in range(0, len(raw), 2):
                    obj[raw[i]] = raw[i + 1]
                stack.append(obj)
            elif op == "LIST_GET":
                idx = stack.pop()
                arr = stack.pop()
                stack.append(arr[idx])
            elif op == "INDEX_GET":
                idx = stack.pop()
                target = stack.pop()
                if isinstance(target, (list, str)):
                    stack.append(target[int(idx)])
                elif isinstance(target, dict):
                    stack.append(target[idx])
                else:
                    raise YasnyError(f"INDEX_GET не поддерживается для типа {type(target).__name__}", path=self.path)
            elif op == "INDEX_SET":
                value = stack.pop()
                idx = stack.pop()
                target = stack.pop()
                if isinstance(target, list):
                    target[int(idx)] = value
                elif isinstance(target, dict):
                    target[idx] = value
                else:
                    raise YasnyError(f"INDEX_SET не поддерживается для типа {type(target).__name__}", path=self.path)
                stack.append(value)
            elif op == "LEN":
                value = stack.pop()
                stack.append(len(value))
            elif op == "HALT":
                return None
            else:
                raise YasnyError(f"Неизвестная инструкция VM: {op}", path=self.path)

        return None

    def _builtin_print(self, args: list[object]) -> object:
        print(*[self._format_value(v) for v in args])
        return None

    def _builtin_len(self, args: list[object]) -> object:
        return len(args[0])

    def _builtin_range(self, args: list[object]) -> object:
        start, end = args
        return list(range(int(start), int(end)))

    def _builtin_input(self, args: list[object]) -> object:
        return input()

    def _pop2(self, stack: list[object]) -> tuple[object, object]:
        b = stack.pop()
        a = stack.pop()
        return b, a

    def _format_value(self, value: object) -> object:
        if value is True:
            return "истина"
        if value is False:
            return "ложь"
        if value is None:
            return "пусто"
        if isinstance(value, list):
            return "[" + ", ".join(str(self._format_value(v)) for v in value) + "]"
        if isinstance(value, dict):
            parts = [f"{self._format_value(k)}: {self._format_value(v)}" for k, v in value.items()]
            return "{ " + ", ".join(parts) + " }"
        return value
