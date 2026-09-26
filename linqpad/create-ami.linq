<Query Kind="Program">
  <NuGetReference>AWSSDK.EC2</NuGetReference>
  <Namespace>Amazon</Namespace>
  <Namespace>Amazon.EC2</Namespace>
  <Namespace>Amazon.EC2.Model</Namespace>
  <Namespace>Amazon.EC2.Util</Namespace>
  <Namespace>Amazon.Util</Namespace>
  <Namespace>System.Threading.Tasks</Namespace>
  <Namespace>System.Net</Namespace>
  <Namespace>System.Collections.Immutable</Namespace>
</Query>

async Task Main()
{
	using (AmazonEC2Client ec2 = new AmazonEC2Client(RegionEndpoint.USWest2))
	{
		DescribeInstancesResponse a1 = await ec2.DescribeInstancesAsync();
		
		string[] names = new string[] { "frodo", "sam", "merry", "pippin" };
		
		var ilist = names.SelectMany(namePrefix => a1.Reservations.SelectMany(r => r.Instances).Where(i => i.Tags.Any(t => t.Key == "Name" && t.Value.StartsWith(namePrefix, StringComparison.InvariantCultureIgnoreCase))).Select(inst => new Tuple<string, Instance>(inst.Tags.First(t => t.Key == "Name").Value, inst))).ToImmutableList();
		
		ImmutableDictionary<string, Instance> idict = ImmutableDictionary<string, Instance>.Empty;
		
		foreach(Tuple<string, Instance> i in ilist)
		{
			if (idict.ContainsKey(i.Item1))
			{
				throw new Exception($"Duplicate instance name {i.Item1}");
			}
			else
			{
				idict = idict.Add(i.Item1, i.Item2);
			}
		}
		
		ImmutableHashSet<string> volumes = ilist.SelectMany(i => i.Item2.BlockDeviceMappings.Select(bdm => bdm.Ebs.VolumeId)).ToImmutableHashSet();
		
		DescribeVolumesRequest qv = new DescribeVolumesRequest(volumes.ToList());
		
		DescribeVolumesResponse av = await ec2.DescribeVolumesAsync(qv);
		
		ImmutableDictionary<string, int> volumeIndex = Enumerable.Range(0, av.Volumes.Count).Select(i => new KeyValuePair<string, int>(av.Volumes[i].VolumeId, i)).ToImmutableDictionary();
		
		ImmutableDictionary<StringPair, string> volumeNames = ImmutableDictionary<StringPair, string>.Empty;
		
		foreach(Tuple<string, Instance> i in ilist)
		{
			foreach(InstanceBlockDeviceMapping ibdm in i.Item2.BlockDeviceMappings)
			{
				bool added = false;
				if (volumeIndex.ContainsKey(ibdm.Ebs.VolumeId))
				{
					Volume v = av.Volumes[volumeIndex[ibdm.Ebs.VolumeId]];
					Tag? t = v.Tags.FirstOrDefault(t => t.Key == "Name");
					if (t != null)
					{
						volumeNames = volumeNames.Add(new StringPair(i.Item2.InstanceId, ibdm.DeviceName), t.Value);
						added = true;
					}
				}
				if (!added)
				{
					string dname = ibdm.DeviceName.Replace('/', '_');
					volumeNames = volumeNames.Add(new StringPair(i.Item2.InstanceId, ibdm.DeviceName), dname);
				}
			}
		}
		
		string dateString = DateTime.Now.ToString("yyyyMMdd_HHmm");

        ImmutableDictionary<int, CreateImageResponse> createResponses = ImmutableDictionary<int, CreateImageResponse>.Empty;

		foreach (int i in Enumerable.Range(0, ilist.Count))
		{
			CreateImageRequest q2 = new CreateImageRequest(ilist[i].Item2.InstanceId, ilist[i].Item1 + "_" + dateString);
			CreateImageResponse a2 = await ec2.CreateImageAsync(q2);
			createResponses = createResponses.Add(i, a2);
		}
		
		createResponses.Dump();
		
		await Task.Delay(30000);

		foreach (KeyValuePair<int, CreateImageResponse> kvp in createResponses)
		{
			CreateImageResponse a2 = kvp.Value;
			
			DescribeImagesRequest q3 = new DescribeImagesRequest()
			{
				ImageIds = new string[] { a2.ImageId }.ToList()
			};

			DescribeImagesResponse a3 = await ec2.DescribeImagesAsync(q3);
			
			await ec2.CreateTagsAsync(new string[] { a2.ImageId }.ToList(), new Tag[] { new Tag() { Key = "Group", Value = "linuxdev" } }.ToList());

			foreach (BlockDeviceMapping bdm in a3.Images[0].BlockDeviceMappings)
			{
				var volumeNameKey = new StringPair(ilist[kvp.Key].Item2.InstanceId, bdm.DeviceName);
				if (volumeNames.ContainsKey(volumeNameKey))
				{
					await ec2.CreateTagsAsync
					(
						new string[] { bdm.Ebs.SnapshotId }.ToList(),
						new Tag[]
						{
						new Tag()
						{
							Key = "Name",
							Value = volumeNames[volumeNameKey] + "_" + dateString,
						},
						new Tag()
						{
							Key = "Group",
							Value = "linuxdev"
						}
						}
						.ToList()
					);
				}
			}
			
			await ec2.CreateTagsAsync(new string[] { a2.ImageId }.ToList(), new Tag[] { new Tag() { Key = "Group", Value = "linuxdev" } }.ToList());
		}
	}
}

public class StringPair : IEquatable<StringPair>, IComparable<StringPair>
{
	public string A { get; private set; }
	public string B { get; private set; }

    public StringPair(string a, string b) { A = a; B = b; }
	
	public override int GetHashCode()
	{
		return (A + "..." + B).GetHashCode();
	}

	public override bool Equals(object obj)
	{
		if (obj == null) return false;
		
		if (obj is StringPair sp) return this.Equals(sp);
		else return false;
	}
	
	public int CompareTo(StringPair other)
	{
		int a1 = A.CompareTo(other.A);
		if (a1 != 0) return a1;
		a1 = B.CompareTo(other.B);
		return a1;
	}

	public bool Equals(StringPair other)
	{
		return this.CompareTo(other) == 0;
	}
}

public static class Extensions
{
	public static async Task<HttpStatusCode> CreateTagsAsync(this AmazonEC2Client client, List<string> resources, List<Tag> tags)
	{
		var q = new CreateTagsRequest()
		{
			Resources = resources,
			Tags = tags,
		};

		try
		{
			var a = await client.CreateTagsAsync(q);

			return a.HttpStatusCode;
		}
		catch(Exception exc)
		{
			q.Dump();
			exc.Dump();
			
			return HttpStatusCode.BadRequest;
		}
	}
}