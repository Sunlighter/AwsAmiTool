<!-- -*- coding: utf-8; fill-column: 118 -*- -->

# create-ami LINQPad Query

This LINQPad query creates AMIs with the characteristics that the AWS AMI Tool is looking for when it&rsquo;s
deleting.

You should replace the RegionEndpoint with the region endpoint that you use, and the names with the names that you
want to create AMIs for.

Importantly: the names are prefixes, so if you specify `pippin` it will pick up `pippin3` etc.

The deletion tool groups AMIs by the prefixes it discovers.

With both this script and the deletion tool, the AWS SDK obtains credentials in its usual way.
