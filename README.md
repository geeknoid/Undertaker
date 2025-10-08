# Undertaker

This tool is designed to help discover dead code, along with various other legacy artifacts, within a set of compiled .NET assemblies.

## Installing

Use:

```bash
dotnet tool install undertaker --global
```

## How It Works

You point this tool at a folder full of .NET assemblies. It loads these assemblies and
builds an in-memory graph that represents all the symbols in those assemblies and which
other symbols they reference. Based on this graph, you can then output a report showing
the set of defined symbols which are never referenced (dead symbols), along with a report
showing the defined symbols that are referenced (alive symbols) and which other symbol
references them. Various other reports are also available, including a report on assemblies that
are never referenced, a report on public symbols that could be made internal, and a report
on needless uses of [InternalsVisibleTo]. You can also output a full layer cake of dependencies
and a Mermaid-based assembly dependency diagram.

## Options

```text
sage:
  Undertaker [<input folders>...] [options]

Arguments:
  <input folders>  Paths to folders containing the assemblies to analyze.

Options:
  -ra, --root-assemblies <text file>                      Path to a text file listing assemblies to be treated as roots, one assembly name per line (with or without
                                                          a .dll extension)
  -rs, --reflection-symbols <text file>                   Path to a text file listing symbols accessed through reflection, with each line in the form of
                                                          `assembly-name:fully-qualified-symbol-name`
  -tma, --test-method-attributes <text file>              Path to a text file listing all the attributes that can mark a method as a test, one per line
  -rma, --reflection-marker-attributes <text file>        Path to a text file listing all the attributes that can mark a method as being used from reflection, one
                                                          per line
  -ds, --dead-symbols <output folder>                     Directory path where to emit the per-assembly reports on dead symbols
  -as, --alive-symbols <output folder>                    Directory path where to emit the per-assembly reports on alive symbols
  -abts, --alive-by-test-symbols <output folder>          Directory path where to emit the per-assembly reports on symbols kept alive only by test methods
  -nps, --needlessly-public-symbols <output folder>       Directory path where to emit the per-assembly reports on public symbols which could be made internal
  -nivt, --needless-internals-visible-to <output folder>  Directory path where to emit the per-assembly reports on needless uses of [InternalsVisibleTo]
  -ua, --unreferenced-assemblies <output file>            Path of the report to produce on completely unreferenced assemblies
  -uaa, --unanalyzed-assemblies <output file>             Path of the report to produce on assemblies which were referenced but not analyzed
  -da, --duplicate-assemblies <output file>               Path of the report to produce on assemblies which were found multiple times as input
  -urs, --unreferenced-symbols <output folder>            Directory path where to emit the per-assembly reports on completely unreferenced symbols
  -alc, --assembly-layer-cake <output file>               Path of the assembly layer cake to produce
  -dd, --dependency-diagram <output file>                 Path of the Mermaid-based assembly dependency diagram to produce
  -cle, --continue-on-load-errors                         Proceed to the analysis and output phases even if some assemblies didn't load
  -v, --verbose                                           Output progress reports
  -csv                                                    Switch some output files from JSON to CSV format
  -?, -h, --help                                          Show help and usage information
  --version                                               Show version information
```

* `<input-folders>` are the paths to one or more folders containing all the assemblies to analyze. The tool will
  analyze any files with the `*.dll` and `*.exe` extensions, skipping any files that are not .NET assemblies.
  The tool recursively visits all subfolders, looking for files to analyze.

* `--root-assemblies` lets you specify the set of root assemblies. This is a text file
  containing the names of assemblies, one per line.

* `--reflection-symbols` lets you specify a text file containing a list of symbols that are accessed
  through reflection. Each line in the file should be in the form of `assembly-name:fully-qualified-symbol-name`.
  If you can modify your source code, instead of using this approach, we recommend instead applying the
  [DynamicallyAccessedMembers] attribute in your code to indicate specific symbols are used by reflection.

* `--test-method-attributes` lets you specify the set of attributes that mark a method as a test. This is a text file
  containing the fully qualified names of the attributes, one per line. If this is not supplied, a default set of
  well-known names is used.

* `--reflection-marker-attributes` lets you specify the set of attributes that mark a method as a being used by reflection. This is a text file
  containing the fully qualified names of the attributes, one per line. If this is not supplied, a default set of
  well-known names is used.

* `--dead-symbols` lets you specify the directory path where per-assembly reports on dead symbols
  should be written. These reports contain a list of all the symbols that are defined in the
  assemblies but are never referenced by any other symbol.

* `--alive-symbols` lets you specify the directory path where per-assembly reports on alive symbols
  should be written. These reports contain a list of all the symbols that are defined in the
  assemblies and are referenced by other symbols.

* `--alive-by-test-symbols` lets you specify the directory path where per-assembly reports on symbols alive only due
  to being used from test methods should be written.

* `--needlessly-public-symbols` lets you specify the directory path where per-assembly reports on needlessly
  public symbols should be written. These reports contain a list of all the symbols that are
  defined as public but are never referenced by any other assembly and so could be made internal or private.

* `--needless-internals-visible-to` lets you specify the directory path where the reports
  on needless uses of [InternalsVisibleTo] should be written.

* `--unreferenced-assemblies` lets you specify the path to the file where the report on unreferenced
  assemblies should be written. This report contains a list of all the assemblies that were loaded as input
  but don't contain any symbols reachable from the roots.

* `--unreferenced-symbols` lets you specify the path to the file where the report on unreferenced
  symbols should be written. This report contains a list of all the symbols that were defined in the
  assemblies but are never referenced by any other symbol. Whereas --dead-symbols reports full graphs
  of symbols as being dead, this report just lists symbols that are completely unused. You can remove
  any of these symbols from your source code in any order since truly nothing is using them.

* `--unanalyzed-assemblies` lets you specify the path to the file where the report on unanalyzed
  assemblies should be written. This report contains a list of all the assemblies that were referenced by
  an analyzed assembly, but were not themselves analyzed (the list doesn't include core .NET libraries
  e.g. System.* and Microsoft.Extensions.*). Look in this list to see if you're missing some assemblies which
  should be part of the set you want to be analyzed but somehow didn't get included.

* `--assembly-layer-cake` lets you specify the path to the file where the full layer cake of dependencies
  should be written. Each assembly in a layer only depends on assemblies in lower layers.

* `--dependency-diagram` lets you specify the path to the file where the assembly dependency diagram in
  Mermaid format should be written. This diagram shows the dependencies between the assemblies in a
  visual format, making it easier to understand the relationships between them.

* `--graph-dumps` lets you specify the directory path where the internal graph dumps should be written.
  These are text files, one per processed assembly, containing the graph of all the symbols and their references.
  This is useful for debugging and understanding the internal workings of this tool.

* `--continue-on-load-errors` lets you specify that the program should continue to run even if some assemblies
  fail to load.

* `--verbose` lets you specify that the program should output progress reports as it runs. This is useful for
  understanding what the tool is doing and how long it will take to complete.

* `-csv` switch some of the output files from JSON format to CSV format.

If you don't specify any of the explicit output options, the tool will default to generating output files in the
current working directory with the following names:

* `dead-symbols/`
* `alive-symbols/`
* `needlessly-public-symbols/`
* `needless-internals-visible-to/`
* `unreferenced-assemblies.json`
* `unanalyzed-assemblies.txt`
* `duplicate-assemblies.json`
* `assembly-layer-cake.json`
* `dependency-diagram.mmd`

## Roots

The analysis to discover dead code depends on knowing the roots, the symbols that are
known to be necessary. It is from those necessary symbols that the dead symbol analysis
begins. There are three kinds of roots:

* Any static method called `Main` in any assembly is considered a root.

* Test methods for a variety of test frameworks.

* You can provide a list of assemblies as _root assemblies_. Any public symbol exposed by
  these assemblies are considered roots. Any assemblies in the set which are considered
  part of a public API should normally be flagged as root assemblies.

Symbols are considered alive if they are referenced in some way by walking the set of
symbols in the assemblies, starting from any root.

## Limitations

Undertaker does a pretty good job at finding most of the dead code in a code base, but there are some things it can't help with:

* **Configuration-Driven Dead Code**. If you have code that only runs when a particular configuration or environment is active, and
  the specific configuration or environment is never actually used, the tool won't tell you about the dead code. This happens in
  a large code base following experiments which have been concluded but the unused code path didn't get removed at the end of the
  experiment.

* **Conditional Compilation**. Code that is compiled out via `#if` will never be flagged as dead even though it might never be used.

* **Unused Public APIs in Root Assemblies**. All public symbols of root assemblies are considered alive even if they are
  never used.

* **Unused Public REST/gRPC APIs**. If your assemblies expose a dead web API, the tool won't be able to tell you about it. This is
  because the tool doesn't analyze the HTTP requests and responses, so it can't tell if a particular API is actually used or not.

* **Enum Members and Const Values**. Undertaker cannot detect unused enum members or const values.

* **Conflicting Assembly Versions**. If you have multiple versions of the same assembly in the input folder, the tool will load the first
  version it encounters and will skip other versions. This could lead to discrepancies in the analysis.

## False Positives

In case Undertaker seems to be reporting code as being dead while you know it isn't dead, there are a few things to do that can hopefully
improve the situation:

* **Data Completeness**. Make sure you give the tool the full transitive set of assemblies your code depends on. If you miss some of these,
  you will get some false positives. You can use `dotnet publish` in self-contained mode to create a folder with your code and all of its
* dependencies included: `dotnet publish --self-contained true`.

* **Dead Symbol Graphs**. Undertaker reports graphs of dead symbol. If you have functions A calls B calls C, it might look as though C is in use
  (since B is calling it) but the whole graph of A+B+C is dead and can be fully deleted. So individual symbols might not be dead, but taken
  as a graph many related symbols can be overall dead.

* **Unreferenced Symbols**. Undertaker reports unreferenced symbols as distinct from dead symbols. In the A+B+C case above, only A would be
  reported as an unreferenced symbol. Unreferenced symbols can generally be deleted in isolation, one by one. Whereas dead symbols may need
  to be deleted as a whole in order to remove full graphs of dependencies.

* **Reflection**. Undertaker gets confused by reflection. If you have symbols that are accessed through reflection, you can apply the attribute
  [DynamicallyAccessedMembers] to the class/method to indicate it it accessed via reflection, and then Undertaker will recognize treat it as in-use.

* **Public APIs**. If you have public APIs that are intended to be used by customers, you should call those out by creating a file listing the names
  of those assemblies and using the --root-assemblies option to give this file to Undertaker.

* **Test-Only APIs**. If you have APIs that are only used by tests, include the test assemblies in the set of assemblies being analyzed.
Undertaker will then consider those symbols alive and will list them in a distinct "alive because of tests" report.

## Ideas

* Flag assemblies that use reflection since they might be cheating and
have dependencies on otherwise dead symbols.

* Can the symbol info include file & line numbers?

* Internal symbols should be considered as roots when an assembly has InternalsVisibleTo to an assembly not under analysis.

* We could detect when a virtual method can be made abstract since all derived types reimplement the method without ever calling
  the base implementation.
