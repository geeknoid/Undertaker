# Undertaker

This tool is designed to help discover dead code within a set of compiled .NET assemblies.

## How It Works

You point this tool at a directory full of .NET assemblies. It loads these assemblies and
builds an in-memory graph that represents all the symbols in those assemblies and which 
other symbols they reference. Based on this graph, you can then output a report showing
the set of defined symbols which are never referenced (dead symbols), along with a report
showing the defined symbols that are referenced (alive symbols) and which other symbol
references them.

## Options

```text
Usage:
  Undertaker <assemblies> [options]

Arguments:
  <assemblies>  Path to folder containing all the assemblies to work with.

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
  ``` 

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

The program doesn't identity unused const values or unused enum values (but unused enum types are identified).

## Ideas

* Flag assemblies that use reflection since they might be cheating and
have dependencies on otherwise dead symbols.

* Can the symbol info include file & line numbers?
