/* Inspect a known game function in a private Ghidra project. */
import ghidra.app.decompiler.DecompInterface;
import ghidra.app.script.GhidraScript;
import ghidra.program.model.address.Address;
import ghidra.program.model.listing.Function;

public class InspectPcFunction extends GhidraScript {
    @Override
    protected void run() throws Exception {
        String[] args = getScriptArgs();
        if (args.length != 2 || !args[0].equalsIgnoreCase(currentProgram.getExecutableSHA256())) {
            throw new IllegalArgumentException("Pass the expected retail SHA-256 and a virtual address.");
        }
        Address entry = toAddr(Long.decode(args[1]));
        Function function = getFunctionAt(entry);
        if (function == null) {
            disassemble(entry);
            function = createFunction(entry, null);
        }
        if (function == null) throw new IllegalStateException("No function at " + entry);
        println("PC_FUNCTION " + function.getEntryPoint() + " " + function.getName() +
                " body_bytes=" + function.getBody().getNumAddresses());
        DecompInterface decompiler = new DecompInterface();
        try {
            if (!decompiler.openProgram(currentProgram)) throw new IllegalStateException(decompiler.getLastMessage());
            var result = decompiler.decompileFunction(function, 60, monitor);
            if (!result.decompileCompleted()) throw new IllegalStateException(result.getErrorMessage());
            println(result.getDecompiledFunction().getC());
        } finally {
            decompiler.dispose();
        }
    }
}
