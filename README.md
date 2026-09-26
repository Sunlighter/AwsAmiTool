<!-- -*- coding: utf-8; fill-column: 118 -*- -->

# AWS AMI Tool

C# tool to select and delete tagged AMIs; command language built with LrParserGenLib.

This tool deletes old AWS AMIs, which can be selected and deselected by the user. You'd probably have to modify it to
make it useful to you, and as a result, I am not shipping a binary.

Interesting features:

* Uses `HttpListener` and starts a browser to present a UI

* Accepts commands typed into a `textarea`

* Uses [Sunlighter.LrParserGenLib](https://github.com/Sunlighter/LrParserGen) to generate a fluent syntax parser,
  which is then used to build parsers (via combinators) for the commands

* Uses `XmlHttpRequest` to provide real-time updates of deletion progress

* Allows retrying if there are any exceptions

My AMIs have a tag called `Group` and its value has to be `linuxdev` in order for this tool to see them. Other AMIs
are ignored, by design.

A typical usage is:

* `list-regions` (optional)
* `set-region` with a quoted string e.g. `"us-west-2"`
* `get-images`
* `select` and `deselect` as needed, with one or more numbers (comma-delimited), e.g., `1,3`
* `start-deletion` (and watch)
* `show-task-status` (when the action stops)
* `retry-faulted` (if any faulted)

Snapshots are also deleted, but are delayed 30 seconds after their AMIs.

Coloration is weird: red indicates selected (but volumes are not selected), pink indicates deleted, and green
indicates an exception.

## Implementation Notes

The commands are implemented by classes whose names start with `TC_`, and most of them don&rsquo;t take arguments.

This program includes a full set of parser combinators even though the commands don&rsquo;t use them all. (It is
notable that the LR(1) parser is not used to parse the commands, but to build the parser for the commands...)

The parsing tables for the fluent syntax are kept in an embedded resource. If you change the grammar, the code will
detect that the tables are no longer up-to-date, and it will build new tables (which is slow) and then write the new
tables to a file on the desktop. You can then drag that file over into the project directory (in File Explorer, not
Visual Studio) and replace the embedded resource, to prevent the tables from being built again. In a production
application, the tables would always be up-to-date, so no file would be written.

This is not the simplest way to write an AMI deletion tool. It was born out of a LINQPad query which would have only
deleted the AMIs that are selected by default here. I wanted the ability to add or remove a few other AMIs, which
requires a UI. I also wanted to work with JavaScript a little. This program uses a lot of parsing tools that I already
had available.
