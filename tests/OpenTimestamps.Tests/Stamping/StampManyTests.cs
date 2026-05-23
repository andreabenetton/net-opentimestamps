using System.Net;
using OpenTimestamps;
using OpenTimestamps.Attestations;
using OpenTimestamps.Calendars;
using OpenTimestamps.Ops;
using OpenTimestamps.Serialization;
using OpenTimestamps.Stamping;
using Xunit;

namespace OpenTimestamps.Tests.Stamping;

public sealed class StampManyTests
{
    [Fact]
    public async Task Stamps_Three_Files_Producing_Three_Independent_Dtfs_From_One_Calendar_Hit()
    {
        // Three temp files, three distinct contents -> three DTFs that
        // share a single calendar attestation merged onto the merkle root.
        string[] paths = WriteTempFiles("alpha", "beta", "gamma");
        try
        {
            var capture = new CaptureCalendarHandler();
            using var http = new HttpClient(capture);
            var client = new CalendarClient(http, new Uri("https://fake.calendar.example/"));

            var stamper = new StampService(
                nonceProvider: () => new byte[StampService.NonceLength]);  // deterministic
            IReadOnlyList<DetachedTimestampFile> dtfs = await stamper.StampManyAsync(
                paths, [client], quorum: 1);

            Assert.Equal(3, dtfs.Count);
            // Exactly ONE calendar submission for the whole batch.
            Assert.Equal(1, capture.SubmissionCount);

            // Each DTF must verify against its own file (file digest matches)
            // AND must walk its op chain to a node containing the pending
            // attestation (calendar response).
            for (int i = 0; i < paths.Length; i++)
            {
                DetachedTimestampFile dtf = dtfs[i];
                byte[] expected = dtf.FileDigest.ToArray();
                byte[] actual = dtf.FileHashOp.HashFile(paths[i]);
                Assert.Equal(expected, actual);

                // Walk to the attested tip and confirm the calendar attestation
                // is reachable from this DTF too (proves merkle path converges).
                bool hasAtt = dtf.Timestamp
                    .AllAttestations()
                    .Any(t => t.Attestation is PendingAttestation);
                Assert.True(hasAtt,
                    $"DTF for {paths[i]} has no calendar attestation on its merkle-root tip.");
            }
        }
        finally
        {
            foreach (string p in paths)
            {
                if (File.Exists(p)) File.Delete(p);
            }
        }
    }

    [Fact]
    public async Task Single_File_Batch_Bypasses_Merkle_Aggregation()
    {
        string[] paths = WriteTempFiles("solo");
        try
        {
            var capture = new CaptureCalendarHandler();
            using var http = new HttpClient(capture);
            var client = new CalendarClient(http, new Uri("https://fake.calendar.example/"));

            var stamper = new StampService(() => new byte[StampService.NonceLength]);
            IReadOnlyList<DetachedTimestampFile> dtfs = await stamper.StampManyAsync(
                paths, [client], quorum: 1);

            Assert.Single(dtfs);
            Assert.Equal(1, capture.SubmissionCount);
            // The DTF's tree has no OpAppend(sibling)/OpSha256 merkle layer
            // beyond the standard nonce path: digest -> OpAppend(nonce) -> OpSha256.
            // We count Ops nodes: should be exactly 2 (nonce append, sha256).
            int opNodes = dtfs[0].Timestamp.AllNodes().Count() - 1;  // exclude root
            Assert.Equal(2, opNodes);
        }
        finally
        {
            foreach (string p in paths)
            {
                if (File.Exists(p)) File.Delete(p);
            }
        }
    }

    [Fact]
    public async Task Empty_Path_List_Rejected()
    {
        var stamper = new StampService();
        await Assert.ThrowsAsync<ArgumentException>(
            () => stamper.StampManyAsync(Array.Empty<string>(), []));
    }

    private static string[] WriteTempFiles(params string[] contents)
    {
        string[] paths = new string[contents.Length];
        for (int i = 0; i < contents.Length; i++)
        {
            paths[i] = Path.Combine(Path.GetTempPath(), $"ots-bs-{Guid.NewGuid():N}.txt");
            File.WriteAllText(paths[i], contents[i]);
        }
        return paths;
    }

    /// <summary>
    /// Captures each digest submission and replies with a synthetic pending
    /// timestamp rooted at the submitted commitment.
    /// </summary>
    private sealed class CaptureCalendarHandler : HttpMessageHandler
    {
        public int SubmissionCount;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref SubmissionCount);
            byte[] commitment = await request.Content!
                .ReadAsByteArrayAsync(cancellationToken)
                .ConfigureAwait(false);

            // Build a "pending" partial timestamp rooted at the commitment.
            var ts = new Timestamp(commitment);
            ts.Attestations.Add(new PendingAttestation("https://fake.calendar.example"));

            using var ms = new MemoryStream();
            ts.Serialize(new OtsWriter(ms));
            byte[] body = ms.ToArray();

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(body),
            };
        }
    }
}
