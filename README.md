# Undertaker

This tool is designed to help discover dead code within a set of compiled .NET assemblies.

## How It Works

You point this tool at a directory full of .NET assemblies. It loads these assemblies and
builds an in-memory graph that represents all the symbols in those assemblies and which 
other symbols they reference. Based on this graph, you can then output a report showing
the set of defined symbols which are never referenced (dead symbols), along with a report
showing the defined symbols that are referenced (alive symbols) and which other symbol
references them.

## Roots

The analysis to discover dead code depends on knowing the roots, the symbols that are
known to be necessary. It is from those necessary symbols that the dead symbol analysis
begins. There are two kinds of roots:

* Any static method called Main in any assembly is considered a root.

* You can provide a list of assemblies as root assembly. Any public symbol exposed by
these assemblies are considered roots. Any assemblies in the set which are considered
part of a public API should normally be flagged as root assemblies.

Symbols are considered alive if they are referenced in some way by walking the set of
symbols in the assemblies, starting from any root.

## Limitations

The program doesn't identity unused const values or unused enum values (but unused enum types are identified).

## Ideas

* Add another report which flags all non-root public symbols which could be made internal.

* Add another report which flags any unecessary uses of InternalsVisibleTo.

* Add the ability to emit a dependency diagram in mermaid format for all the assemblies.

* Add the ability to emit a layer cake diagram for all the assemblies.

* Flag assemblies that use reflection since they might be cheating and
have dependencies on otherwise dead symbols.

## TODO

* Hasn't yet been tested against .NET Framework assemblies, only .NET Core.

* Doesn't yet have support for C# events and indexers.
