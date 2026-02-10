from __future__ import annotations

import json
import struct
from dataclasses import dataclass

from .diagnostics import YasnError


MAGIC = b"YASNYBC1"


@dataclass(slots=True)
class Instruction:
    op: str
    args: list[object]


@dataclass(slots=True)
class FunctionBC:
    name: str
    params: list[str]
    local_count: int
    instructions: list[Instruction]


@dataclass(slots=True)
class ProgramBC:
    functions: dict[str, FunctionBC]
    entry: FunctionBC
    global_count: int = 0


def encode_program(program: ProgramBC) -> bytes:
    payload = {
        "functions": {name: _encode_function(fn) for name, fn in program.functions.items()},
        "entry": _encode_function(program.entry),
        "global_count": int(program.global_count),
    }
    raw_json = json.dumps(payload, ensure_ascii=False, separators=(",", ":")).encode("utf-8")
    return MAGIC + struct.pack("<I", len(raw_json)) + raw_json


def decode_program(blob: bytes, path: str | None = None) -> ProgramBC:
    if len(blob) < len(MAGIC) + 4:
        raise YasnError("Файл байткода слишком короткий", path=path)
    if blob[: len(MAGIC)] != MAGIC:
        raise YasnError("Неверная сигнатура файла .ybc", path=path)
    (length,) = struct.unpack("<I", blob[len(MAGIC) : len(MAGIC) + 4])
    payload = blob[len(MAGIC) + 4 :]
    if length != len(payload):
        raise YasnError("Некорректная длина полезной нагрузки .ybc", path=path)
    try:
        obj = json.loads(payload.decode("utf-8"))
    except Exception as exc:  # noqa: BLE001
        raise YasnError(f"Не удалось разобрать JSON байткода: {exc}", path=path) from exc

    functions = {name: _decode_function(fn_obj) for name, fn_obj in obj["functions"].items()}
    entry = _decode_function(obj["entry"])
    global_count = int(obj.get("global_count", 0))
    return ProgramBC(functions=functions, entry=entry, global_count=global_count)


def _encode_function(fn: FunctionBC) -> dict[str, object]:
    return {
        "name": fn.name,
        "params": fn.params,
        "local_count": fn.local_count,
        "instructions": [{"op": ins.op, "args": ins.args} for ins in fn.instructions],
    }


def _decode_function(obj: dict[str, object]) -> FunctionBC:
    instructions = [Instruction(op=i["op"], args=list(i["args"])) for i in obj["instructions"]]
    return FunctionBC(
        name=str(obj["name"]),
        params=list(obj["params"]),
        local_count=int(obj["local_count"]),
        instructions=instructions,
    )
