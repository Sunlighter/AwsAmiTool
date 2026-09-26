<!-- -*- coding: utf-8; fill-column: 118 -*- -->

# AWS AMI Tool

This tool deletes old AWS AMIs. You'd probably have to modify it to make it useful to you, and as a result, I am not
shipping a binary.

My AMIs have a tag called `Group` and its value has to be `linuxdev` in order for this tool to see them. Other AMIs
are ignored, by design.

It uses `HttpListener` to host itself, so that a web browser can be its UI. It starts the browser when it starts.

It presents a textarea where you can type a command. (The commands are implemented by classes whose names start with
`TC_`, and most of them don&rsquo;t take arguments.)

It uses the **Sunlighter.LrParserGenLib** to create a fluent syntax for building parser combinators for these
commands. There is a full set of parser combinators even though this program doesn&rsquo;t use them all. (It is
notable that the LR(1) parser is not used to parse the commands, but to build the parser for the commands...)

The parsing tables for the fluent syntax are kept in an embedded resource. If you change the grammar, the code will
detect that the tables are no longer up-to-date, and it will build new tables (which is slow) and then write the new
tables to a file on the desktop. You can then drag that file over into the project directory (in File Explorer, not
Visual Studio) and replace the embedded resource, to prevent the tables from being built again. In a production
application, the tables would always be up-to-date, so no file would be written.

The browser uses long polling (via XHR) to fetch progress indications from the server side. It is possible for the
user to retry if there are any exceptions when deleting an AMI or a snapshot.

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

This is not the simplest way to write an AMI deletion tool. It was born out of a LINQPad query which would have only
deleted the AMIs that are selected by default here. I wanted the ability to add or remove a few other AMIs, which
requires a UI. I also wanted to work with JavaScript a little. This program uses a lot of parsing tools that I already
had available.
