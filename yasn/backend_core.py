from __future__ import annotations

from dataclasses import dataclass
from pathlib import Path

from .pipeline import compile_source
from .vm import VirtualMachine


@dataclass(slots=True)
class BackendKernel:
    source_path: str
    vm: VirtualMachine

    @classmethod
    def from_file(cls, source_path: str | Path) -> "BackendKernel":
        path = Path(source_path).resolve()
        source = path.read_text(encoding="utf-8")
        program = compile_source(source, path=str(path))
        vm = VirtualMachine(program=program, path=str(path))
        return cls(source_path=str(path), vm=vm)

    def list_functions(self) -> list[str]:
        return sorted(self.vm.program.functions.keys())

    def has_function(self, function_name: str) -> bool:
        return function_name in self.vm.program.functions

    def call(
        self,
        function_name: str,
        args: list[object] | None = None,
        reset_state: bool = True,
    ) -> object:
        return self.vm.call_function(function_name, args=args or [], reset_state=reset_state)
