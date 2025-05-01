# Undertaker

This tool is designed to help discover dead code within a set of compiled .NET assemblies.

## How It Works

You point this tool at a folder full of .NET assemblies. It loads these assemblies and
builds an in-memory graph that represents all the symbols in those assemblies and which 
other symbols they reference. Based on this graph, you can then output a report showing
the set of defined symbols which are never referenced (dead symbols), along with a report
showing the defined symbols that are referenced (alive symbols) and which other symbol
references them. Various other reports are also available, including a report on assemblies that
are never referenced, a report on public symbols that could be made internal, and a report
on spurious uses of [InternalsVisibleTo]. You can also output a full layer cake of dependencies
and a Mermaid-based assembly dependency diagram.

## Options

```text
Usage:
  Undertaker <assembly-folder> [options]

Arguments:
  <assembly-folder>  Path to a folder containing all the assemblies to work with.

Options:
  -ra, --root-assemblies <root-assemblies>                  Path to a text file listing assemblies to be treated as
                                                            roots, one assembly name per line
  -ds, --dead-symbols <dead-symbols>                        Path of the report to produce on dead symbols
  -as, --alive-symbols <alive-symbols>                      Path of the report to produce on alive symbols
  -ps, --public-symbols <public-symbols>                    Path of the report to produce on public symbols which could
                                                            be made internal
  -ua, --unreferenced-assemblies <unreferenced-assemblies>  Path of the report to produce on completely unreferenced
                                                            assemblies
  -ivt, --internals-visible-to <internals-visible-to>       Path of the report to produce on spurious uses of
                                                            [InternalsVisibleTo]
  -alc, --assembly-layer-cake <assembly-layer-cake>         Path of the assembly layer cake to produce
  -dd, --dependency-diagram <dependency-diagram>            Path of the Mermaid-based assembly dependency diagram to
                                                            produce
  -gd, --graph-dump <graph-dump>                            Path of the graph dump file to produce
  -cle, --continue-on-load-errors                           Proceed to the analysis and output phases even if some
                                                            assemblies didn't load
  -v, --verbose                                             Output progress reports
```

* `<assembly-folder>` is the path to a folder containing all the assemblies to work with. The tool will
  analyze any files with the `*.dll` and `*.exe` extensions, skipping any files that are not .NET assemblies.
  The tool recursively visits all subfolders, looking for files to analyze.

* `--root-assemblies` lets you specify the set of root assemblies. This is a text file
  containing the names of assemblies, one per line.

* `--dead-symbols` lets you specify the path to the file where the report on dead symbols
  should be written. This report contains a list of all the symbols that are defined in the
  assemblies but are never referenced by any other symbol.

* `--alive-symbols` lets you specify the path to the file where the report on alive symbols
  should be written. This report contains a list of all the symbols that are defined in the
  assemblies and are referenced by other symbols.

* `--public-symbols` lets you specify the path to the file where the report on needlessly
  public symbols should be written. This report contains a list of all the symbols that are
  defined as public but are never referenced by any other assembly and so could be made internal.

* `--unreferenced-assemblies` lets you specify the path to the file where the report on unreferenced
  assemblies should be written. This report contains a list of all the assemblies that were loaded as input
  but don't contain any symbols reachable from the roots.
 
* `--internals-visible-to` lets you specify the path to the file where the report
  on needless uses of [InternalsVisibleTo] should be written.

* `--assembly-layer-cake` lets you specify the path to the file where the full layer cake of dependencies
  should be written. Each assembly in a layer only depends on assemblies in lower layers.

* `--dependency-diagram` lets you specify the path to the file where the assembly dependency diagram in
  Mermaid format should be written. This diagram shows the dependencies between the assemblies in a
  visual format, making it easier to understand the relationships between them.

* `--graph-dump` lets you specify the path to the file where the internal graph dump should be written.
  This is a text file containing the graph of all the symbols and their references. This is useful for
  debugging and understanding the internal workings of the tool.
	
* `--continue-on-load-errors` lets you specify that the program should continue to run even if some assemblies
  fail to load.

* `--verbose` lets you specify that the program should output progress reports as it runs. This is useful for
  understanding what the tool is doing and how long it will take to complete.

If you don't specify any of the explicit output options, the tool will default to generating output files in the
current working directory with the following names:

* `dead-symbols.json`
* `alive-symbols.json`
* `public-symbols.json`
* `unreferenced-assemblies.json`
* `internals-visible-to.json`
* `assembly-layer-cake.json`
* `dependency-diagram.mmd`

## Roots

The analysis to discover dead code depends on knowing the roots, the symbols that are
known to be necessary. It is from those necessary symbols that the dead symbol analysis
begins. There are two kinds of roots:

* Any static method called `Main` in any assembly is considered a root.

* You can provide a list of assemblies as root assemblies. Any public symbol exposed by
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
 
* **#ifdef code**. Code that is compiled out via #ifdef will never be flagged as dead even though it might never be used.
 
* **Unused Public APIs in Root Assemblies**. All public symbols of root assemblies are considered alive even if they are
  never used.

* **Unused Public REST/gRPC APIs**. If your assemblies expose a dead web API, the tool won't be able to tell you about it. This is
  because the tool doesn't analyze the HTTP requests and responses, so it can't tell if a particular API is actually used or not. 

* *Enum Members and Const Values**. Undertaker cannot detect unused enum members or const values.

## Ideas

* Flag assemblies that use reflection since they might be cheating and
have dependencies on otherwise dead symbols.

* Can the symbol info include file & line numbers?

* Internal symbols should be considered as roots when an assembly has InternalsVisibleTo to an assembly not under analysis.

* We could detect when a virtual method can be made abstract since all derived types reimplement the method without ever calling
  the base implementation. 

* The tool should understand the difference between production and test assemblies. This would enable it to report code that is
  kept alive strictly by virtue of a test.
