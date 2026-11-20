// Inspect an EE function range and its direct callers in a local Ghidra project.
// @category OpenSCCT
import ghidra.app.script.GhidraScript;
import ghidra.program.model.address.Address;
import ghidra.program.model.listing.Instruction;

public class ReviewEeLeaf extends GhidraScript {
    @Override
    public void run() throws Exception {
        String[] args = getScriptArgs();
        if (args.length < 2) {
            throw new IllegalArgumentException("Expected EE entry address, byte size, then optional call sites");
        }
        Address entry = toAddr(Long.decode(args[0]));
        int size = Integer.decode(args[1]);
        if (size <= 0 || size % 4 != 0 || entry.getOffset() % 4 != 0) {
            throw new IllegalArgumentException("Expected an instruction-aligned range");
        }
        println("Executable SHA-256 " + currentProgram.getExecutableSHA256());
        for (int offset = 0; offset < size; offset += 4) {
            Address address = entry.add(offset);
            if (getInstructionAt(address) == null) {
                disassemble(address);
            }
            Instruction instruction = getInstructionAt(address);
            if (instruction == null) {
                throw new IllegalStateException("Missing instruction at " + address);
            }
            println(address + " " + instruction);
        }
        for (int i = 2; i < args.length; i++) {
            Address site = toAddr(Long.decode(args[i]));
            if (getInstructionAt(site) == null) {
                disassemble(site);
            }
            Instruction call = getInstructionAt(site);
            if (call == null || !call.getFlowType().isCall() ||
                    !java.util.Arrays.asList(call.getFlows()).contains(entry)) {
                throw new IllegalStateException("Expected direct call at " + site + " to " + entry);
            }
            println("Confirmed direct call at " + site + " to " + entry);
        }
    }
}
