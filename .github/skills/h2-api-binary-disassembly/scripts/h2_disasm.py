#!/usr/bin/env python3
"""Inspect closed-source H2 API binaries (ELF .so with symbols, or Windows PE DLL).

Dependencies (install into a throwaway dir, then set PYTHONPATH to it):
    python -m pip install --target <tmp> pefile capstone pyelftools

Do NOT rename this file to dis.py: it would shadow the stdlib `dis` module and
break pyelftools imports.

Subcommands (ELF unless noted):
    syms     <lib> [substr ...]       list function symbols (mangled + size)
    dis      <lib> <symbol> ...       full disassembly, call targets resolved
    summary  <lib> <symbol> ...       only callees + send_instr opcode byte
    vtable   <lib> <class-substr>     resolve vtable slots (_ZTV*) to methods
    pe       <dll> <export> [n]       disassemble a PE export (Windows check)
"""
import sys

import capstone


def load_elf(path):
    from elftools.elf.elffile import ELFFile

    elf = ELFFile(open(path, 'rb'))
    symtab = elf.get_section_by_name('.symtab')
    dynsym = elf.get_section_by_name('.dynsym')
    if symtab is None:
        print('WARNING: no .symtab (stripped); only exported .dynsym names are available')
    addr2name, name2sym = {}, {}
    for table in (symtab, dynsym):
        if table is None:
            continue
        for sym in table.iter_symbols():
            if sym['st_value']:
                addr2name.setdefault(sym['st_value'], sym.name)
                name2sym.setdefault(sym.name, sym)

    # Resolve PLT stubs so calls into libc/libusb/exported self-calls get names.
    md = capstone.Cs(capstone.CS_ARCH_X86, capstone.CS_MODE_64)
    md.detail = True
    relplt = elf.get_section_by_name('.rela.plt')
    if relplt is not None and dynsym is not None:
        got2name = {r['r_offset']: dynsym.get_symbol(r['r_info_sym']).name
                    for r in relplt.iter_relocations()}
        for secname in ('.plt.sec', '.plt'):
            plt = elf.get_section_by_name(secname)
            if plt is None:
                continue
            base = plt['sh_addr']
            for ins in md.disasm(plt.data(), base):
                if ins.mnemonic == 'jmp' and 'rip' in ins.op_str:
                    target = ins.address + ins.size + ins.operands[0].mem.disp
                    if target in got2name:
                        stub = ins.address - (ins.address - base) % 16
                        addr2name.setdefault(stub, got2name[target] + '@plt')

    text = elf.get_section_by_name('.text')
    return elf, addr2name, name2sym, text.data(), text['sh_addr'], md


def iter_function(ctx, name):
    _, _, name2sym, tdata, taddr, md = ctx
    sym = name2sym.get(name)
    if sym is None:
        candidates = [n for n in name2sym if name in n]
        sys.exit(f'symbol {name!r} not found; candidates: {candidates[:10]}')
    start, size = sym['st_value'], sym['st_size']
    return md.disasm(tdata[start - taddr:start - taddr + size], start)


def call_name(addr2name, ins):
    try:
        return addr2name.get(int(ins.op_str, 16), ins.op_str)
    except ValueError:
        return 'indirect ' + ins.op_str


def cmd_syms(path, *filters):
    _, _, name2sym, *_ = load_elf(path)
    funcs = [(n, s) for n, s in name2sym.items() if s['st_info']['type'] == 'STT_FUNC']
    print('function symbols:', len(funcs))
    for n, s in sorted(funcs):
        if not filters or any(f.lower() in n.lower() for f in filters):
            print(f"{hex(s['st_value'])} {s['st_size']:6d} {n}")


def cmd_dis(path, *names):
    ctx = load_elf(path)
    for name in names:
        print('=====', name)
        for ins in iter_function(ctx, name):
            note = ''
            if ins.mnemonic == 'call' or ins.mnemonic.startswith('j'):
                resolved = call_name(ctx[1], ins)
                if resolved != ins.op_str:
                    note = ' ; ' + resolved
            print(hex(ins.address), ins.mnemonic, ins.op_str, note)


NOISE = ('stack_chk', 'Unwind', '_ZdlPv', 'memcpy', 'memset', 'St6vector')


def cmd_summary(path, *names):
    ctx = load_elf(path)
    for name in names:
        print('==', name)
        last_opcode = None
        for ins in iter_function(ctx, name):
            # Instr packets are built on the stack; byte 0 is the opcode.
            if (ins.mnemonic == 'mov' and ins.op_str.startswith('byte ptr [rsp')
                    and '], 0x' in ins.op_str):
                last_opcode = ins.op_str.split(', ')[-1]
            if ins.mnemonic != 'call':
                continue
            callee = call_name(ctx[1], ins)
            if any(n in callee for n in NOISE):
                continue
            if 'send_instr' in callee:
                callee += f' opcode={last_opcode}'
            print('   ', callee)


def cmd_vtable(path, needle):
    elf, addr2name, name2sym, *_ = load_elf(path)
    dynsym = elf.get_section_by_name('.dynsym')
    slots = {}
    for sec in elf.iter_sections():
        if sec['sh_type'] != 'SHT_RELA':
            continue
        for r in sec.iter_relocations():
            if r['r_info_type'] == 8:  # R_X86_64_RELATIVE
                slots[r['r_offset']] = addr2name.get(r['r_addend'], hex(r['r_addend']))
            elif r['r_info_type'] == 1 and dynsym is not None:  # R_X86_64_64
                slots[r['r_offset']] = dynsym.get_symbol(r['r_info_sym']).name
    for name, sym in sorted(name2sym.items()):
        if not name.startswith('_ZTV') or needle.lower() not in name.lower():
            continue
        print(name)
        # Itanium ABI: [offset-to-top][typeinfo] then virtual function slots.
        for i in range(2, sym['st_size'] // 8):
            print(f"   slot {hex((i - 2) * 8)} {slots.get(sym['st_value'] + i * 8, '?')}")


def cmd_pe(path, export, count='120'):
    import pefile

    pe = pefile.PE(path)
    base = pe.OPTIONAL_HEADER.ImageBase
    exports = {e.name.decode(): e.address for e in pe.DIRECTORY_ENTRY_EXPORT.symbols if e.name}
    if export not in exports:
        sys.exit(f'export {export!r} not found')
    mode = capstone.CS_MODE_64 if pe.FILE_HEADER.Machine == 0x8664 else capstone.CS_MODE_32
    md = capstone.Cs(capstone.CS_ARCH_X86, mode)
    rva = exports[export]
    addr2export = {base + a: n for n, a in exports.items()}
    for i, ins in enumerate(md.disasm(pe.get_data(rva, 0x2000), base + rva)):
        note = ''
        if ins.mnemonic == 'call':
            try:
                note = ' ; ' + addr2export.get(int(ins.op_str, 16), 'internal (no symbol)')
            except ValueError:
                note = ' ; indirect'
        print(hex(ins.address), ins.mnemonic, ins.op_str, note)
        if ins.mnemonic == 'ret' or i + 1 >= int(count):
            break


COMMANDS = {'syms': cmd_syms, 'dis': cmd_dis, 'summary': cmd_summary,
            'vtable': cmd_vtable, 'pe': cmd_pe}

if __name__ == '__main__':
    if len(sys.argv) < 3 or sys.argv[1] not in COMMANDS:
        sys.exit(__doc__)
    COMMANDS[sys.argv[1]](*sys.argv[2:])
