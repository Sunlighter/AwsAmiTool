<!-- -*- coding: utf-8; fill-column: 118 -*- -->

# AWS AMI Tool

This is a tool I wrote so that I could delete old AWS AMIs. You'd probably have to modify it to make it useful to you,
and as a result, I am not shipping a binary.

My AMIs have a tag called `Group` and its value has to be `linuxdev` in order for this tool to see them. Other AMIs
are ignored, by design.

It uses `HttpListener` to host itself, so that a web browser can be its UI. It starts the browser when it starts.

It presents a textarea where you can type a command. (To find out what the commands are, look for the classes whose
names start with `TC_`, most of them don&rsquo;t take arguments.)

It uses the **Sunlighter.LrParserGenLib** to create a fluent syntax for building parser combinators for these
commands. There is a full set of parser combinators even though this program doesn&rsquo;t use them all.

The browser uses long polling (via XHR) to fetch progress indications from the server side. It is possible to retry if
there are any exceptions.
