using Discord.WebSocket;
using Microsoft.Reactive.Testing;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive;
using System.Text;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

namespace DiscordDice.Tests.Commands
{
    [TestClass]
    public class MessageEntranceTest
    {
        static Recorded<Notification<T>> OnNext<T>(long ticks, T value)
        {
            return ReactiveTest.OnNext(ticks, value);
        }

        static Func<Recorded<Notification<Response>>, bool> ResponseFilter(Func<Response, bool> responsePredicate = null)
        {
            return r =>
            {
                if (!r.Value.HasValue)
                {
                    return true;
                }
                if (responsePredicate == null)
                {
                    return r.Value.Value.Type != ResponseType.None;
                }
                return responsePredicate(r.Value.Value);
            };
        }


        static class AssertEx
        {
            public static void AreResponsesEquivalent(IEnumerable<Recorded<Notification<Response>>> actual, IEnumerable<Recorded<Notification<Response>>> expected)
            {
                ReactiveAssert.AreElementsEqual(
                    actual.Where(ResponseFilter()),
                    expected.Where(ResponseFilter()));
            }

            public static void TypesAre(IEnumerable<Recorded<Notification<Response>>> actual, params ResponseType[] expected)
            {
                var actual_ =
                    actual
                    .Where(ResponseFilter())
                    .Select(r => r.Value.Value.Type)
                    .ToArray();
                CollectionAssert.AreEqual(actual_, expected);
            }

            public static void TypeCountsAre(IEnumerable<Recorded<Notification<Response>>> actual, int sayCount = 0, int cautionCount = 0)
            {
                var actual_ =
                    actual
                    .Where(ResponseFilter())
                    .Select(r => r.Value.Value.Type)
                    .ToArray();
                var expected = 
                    Enumerable.Repeat(ResponseType.Say, sayCount)
                    .Concat(Enumerable.Repeat(ResponseType.Caution, cautionCount))
                    .ToArray();
                CollectionAssert.AreEquivalent(actual_, expected);
            }

            public static void ExactlyOneSay(IEnumerable<Recorded<Notification<Response>>> actual)
            {
                TypesAre(actual, ResponseType.Say);
            }

            public static void AllSay(IEnumerable<Recorded<Notification<Response>>> actual, int count)
            {
                Assert.AreEqual(actual.Where(ResponseFilter(r => r.Type == ResponseType.Say)).Count(), count);
            }

            public static void ExactlyOneCaution(IEnumerable<Recorded<Notification<Response>>> actual)
            {
                TypesAre(actual, ResponseType.Caution);
            }

            public static void IsEmpty(IEnumerable<Recorded<Notification<Response>>> actual)
            {
                AreResponsesEquivalent(actual, new Recorded<Notification<Response>>[] { });
            }
        }

        static (MessageEntrance, ITestableObserver<Response>, TestConfig) Init()
        {
            var config = new TestConfig { UtcNow = new DateTimeOffset(2000, 1, 1, 0, 0, 0, 0, TimeSpan.Zero) };

            using (var context = MainDbContext.GetInstance(config))
            {
                context.Database.EnsureDeleted();
                context.Database.EnsureCreated();
            }

            var testScheduler = new TestScheduler();
            var testObserver = testScheduler.CreateObserver<Response>();
            var entrance = new MessageEntrance(TestLazySocketClient.Default, config);
            entrance.ResponseSent.Subscribe(testObserver);

            return (entrance, testObserver, config);
        }


        [TestMethod]
        public async Task Plain_NoMentionTest()
        {
            ulong botCurrentUserId = TestLazySocketUser.MyBot.Id;
            var (allCommands, testObserver, _) = Init();

            await allCommands.ReceiveMessageAsync(TestLazySocketMessage.CreateNoMentionMessage("hello"), botCurrentUserId);
            AssertEx.IsEmpty(testObserver.Messages);
        }

        [TestMethod]
        public async Task Plain_NotMentionedTest()
        {
            ulong botCurrentUserId = TestLazySocketUser.MyBot.Id;
            var (allCommands, testObserver, _) = Init();

            await allCommands.ReceiveMessageAsync(TestLazySocketMessage.CreateNotMentionedMessage("hello"), botCurrentUserId);
            AssertEx.IsEmpty(testObserver.Messages);
        }


        [TestMethod]
        public async Task Roll_NoMentionTest()
        {
            ulong botCurrentUserId = TestLazySocketUser.MyBot.Id;
            var (allCommands, testObserver, _) = Init();

            await allCommands.ReceiveMessageAsync(TestLazySocketMessage.CreateNoMentionMessage("1d100"), botCurrentUserId);
            AssertEx.ExactlyOneSay(testObserver.Messages);
        }

        [TestMethod]
        public async Task RollByFullWidth_NoMentionTest()
        {
            ulong botCurrentUserId = TestLazySocketUser.MyBot.Id;
            var (allCommands, testObserver, _) = Init();

            await allCommands.ReceiveMessageAsync(TestLazySocketMessage.CreateNoMentionMessage("１ｄ１００"), botCurrentUserId);
            AssertEx.ExactlyOneSay(testObserver.Messages);
        }

        [TestMethod]
        public async Task Roll_MentionedTest()
        {
            ulong botCurrentUserId = TestLazySocketUser.MyBot.Id;
            var (allCommands, testObserver, _) = Init();

            await allCommands.ReceiveMessageAsync(TestLazySocketMessage.CreateMentionedMessage("1d100"), botCurrentUserId);
            AssertEx.ExactlyOneSay(testObserver.Messages);
        }

        [TestMethod]
        public async Task Roll_NotMentionedTest()
        {
            ulong botCurrentUserId = TestLazySocketUser.MyBot.Id;
            var (allCommands, testObserver, _) = Init();

            await allCommands.ReceiveMessageAsync(TestLazySocketMessage.CreateNotMentionedMessage("1d100"), botCurrentUserId);
            AssertEx.IsEmpty(testObserver.Messages);
        }

        [TestMethod]
        public async Task Roll_ByOtherBotTest()
        {
            ulong botCurrentUserId = TestLazySocketUser.MyBot.Id;
            var (allCommands, testObserver, _) = Init();

            await allCommands.ReceiveMessageAsync(TestLazySocketMessage.CreateOtherBotMessage("1d100"), botCurrentUserId);
            AssertEx.IsEmpty(testObserver.Messages);
        }


        [TestMethod]
        public async Task Help_MentionedTest()
        {
            ulong botCurrentUserId = TestLazySocketUser.MyBot.Id;
            var (allCommands, testObserver, _) = Init();

            await allCommands.ReceiveMessageAsync(TestLazySocketMessage.CreateMentionedMessage("help"), botCurrentUserId);
            AssertEx.ExactlyOneSay(testObserver.Messages);
        }

        [TestMethod]
        public async Task Help_NoMentionTest()
        {
            ulong botCurrentUserId = TestLazySocketUser.MyBot.Id;
            var (allCommands, testObserver, _) = Init();

            await allCommands.ReceiveMessageAsync(TestLazySocketMessage.CreateNoMentionMessage("help"), botCurrentUserId);
            AssertEx.IsEmpty(testObserver.Messages);
        }

        [TestMethod]
        public async Task Help_WithOptionTest()
        {
            ulong botCurrentUserId = TestLazySocketUser.MyBot.Id;
            var (allCommands, testObserver, _) = Init();

            await allCommands.ReceiveMessageAsync(TestLazySocketMessage.CreateMentionedMessage("help --foo"), botCurrentUserId);
            AssertEx.ExactlyOneCaution(testObserver.Messages);
        }


        [TestMethod]
        public async Task HWithHyphen_MentionedTest()
        {
            ulong botCurrentUserId = TestLazySocketUser.MyBot.Id;
            var (allCommands, testObserver, _) = Init();

            await allCommands.ReceiveMessageAsync(TestLazySocketMessage.CreateMentionedMessage("-h"), botCurrentUserId);
            AssertEx.ExactlyOneSay(testObserver.Messages);
        }

        [TestMethod]
        public async Task HWithHyphen_NoMentionTest()
        {
            ulong botCurrentUserId = TestLazySocketUser.MyBot.Id;
            var (allCommands, testObserver, _) = Init();

            await allCommands.ReceiveMessageAsync(TestLazySocketMessage.CreateNoMentionMessage("-h"), botCurrentUserId);
            AssertEx.IsEmpty(testObserver.Messages);
        }

        [TestMethod]
        public async Task HWithHyphen_WithOptionTest()
        {
            ulong botCurrentUserId = TestLazySocketUser.MyBot.Id;
            var (allCommands, testObserver, _) = Init();

            await allCommands.ReceiveMessageAsync(TestLazySocketMessage.CreateMentionedMessage("-h --foo"), botCurrentUserId);
            AssertEx.ExactlyOneCaution(testObserver.Messages);
        }

        [TestMethod]
        public async Task HelpWithHyphen_MentionedTest()
        {
            ulong botCurrentUserId = TestLazySocketUser.MyBot.Id;
            var (allCommands, testObserver, _) = Init();

            await allCommands.ReceiveMessageAsync(TestLazySocketMessage.CreateMentionedMessage("--help"), botCurrentUserId);
            AssertEx.ExactlyOneSay(testObserver.Messages);
        }

        [TestMethod]
        public async Task HelpWithHyphen_NoMentionTest()
        {
            ulong botCurrentUserId = TestLazySocketUser.MyBot.Id;
            var (allCommands, testObserver, _) = Init();

            await allCommands.ReceiveMessageAsync(TestLazySocketMessage.CreateNoMentionMessage("--help"), botCurrentUserId);
            AssertEx.IsEmpty(testObserver.Messages);
        }

        [TestMethod]
        public async Task HelpWithHyphen_WithOptionTest()
        {
            ulong botCurrentUserId = TestLazySocketUser.MyBot.Id;
            var (allCommands, testObserver, _) = Init();

            await allCommands.ReceiveMessageAsync(TestLazySocketMessage.CreateMentionedMessage("--help --foo"), botCurrentUserId);
            AssertEx.ExactlyOneCaution(testObserver.Messages);
        }

        [TestMethod]
        public async Task Version_MentionedTest()
        {
            ulong botCurrentUserId = TestLazySocketUser.MyBot.Id;
            var (allCommands, testObserver, _) = Init();

            await allCommands.ReceiveMessageAsync(TestLazySocketMessage.CreateMentionedMessage("version"), botCurrentUserId);
            AssertEx.ExactlyOneSay(testObserver.Messages);
        }

        [TestMethod]
        public async Task Version_NoMentionTest()
        {
            ulong botCurrentUserId = TestLazySocketUser.MyBot.Id;
            var (allCommands, testObserver, _) = Init();

            await allCommands.ReceiveMessageAsync(TestLazySocketMessage.CreateNoMentionMessage("version"), botCurrentUserId);
            AssertEx.IsEmpty(testObserver.Messages);
        }

        [TestMethod]
        public async Task Version_WithOptionTest()
        {
            ulong botCurrentUserId = TestLazySocketUser.MyBot.Id;
            var (allCommands, testObserver, _) = Init();

            await allCommands.ReceiveMessageAsync(TestLazySocketMessage.CreateMentionedMessage("version --foo"), botCurrentUserId);
            AssertEx.ExactlyOneCaution(testObserver.Messages);
        }

        [TestMethod]
        public async Task VWithHyphen_MentionedTest()
        {
            ulong botCurrentUserId = TestLazySocketUser.MyBot.Id;
            var (allCommands, testObserver, _) = Init();

            await allCommands.ReceiveMessageAsync(TestLazySocketMessage.CreateMentionedMessage("-v"), botCurrentUserId);
            AssertEx.ExactlyOneSay(testObserver.Messages);
        }

        [TestMethod]
        public async Task VWithHyphen_NoMentionTest()
        {
            ulong botCurrentUserId = TestLazySocketUser.MyBot.Id;
            var (allCommands, testObserver, _) = Init();

            await allCommands.ReceiveMessageAsync(TestLazySocketMessage.CreateNoMentionMessage("-v"), botCurrentUserId);
            AssertEx.IsEmpty(testObserver.Messages);
        }

        [TestMethod]
        public async Task VWithHyphen_WithOptionTest()
        {
            ulong botCurrentUserId = TestLazySocketUser.MyBot.Id;
            var (allCommands, testObserver, _) = Init();

            await allCommands.ReceiveMessageAsync(TestLazySocketMessage.CreateMentionedMessage("-v --foo"), botCurrentUserId);
            AssertEx.ExactlyOneCaution(testObserver.Messages);
        }


        [TestMethod]
        public async Task VersionWithHyphen_MentionedTest()
        {
            ulong botCurrentUserId = TestLazySocketUser.MyBot.Id;
            var (allCommands, testObserver, _) = Init();

            await allCommands.ReceiveMessageAsync(TestLazySocketMessage.CreateMentionedMessage("--version"), botCurrentUserId);
            AssertEx.ExactlyOneSay(testObserver.Messages);
        }

        [TestMethod]
        public async Task VersionWithHyphen_NoMentionTest()
        {
            ulong botCurrentUserId = TestLazySocketUser.MyBot.Id;
            var (allCommands, testObserver, _) = Init();

            await allCommands.ReceiveMessageAsync(TestLazySocketMessage.CreateNoMentionMessage("--version"), botCurrentUserId);
            AssertEx.IsEmpty(testObserver.Messages);
        }

        [TestMethod]
        public async Task VersionWithHyphen_WithOptionTest()
        {
            ulong botCurrentUserId = TestLazySocketUser.MyBot.Id;
            var (allCommands, testObserver, _) = Init();

            await allCommands.ReceiveMessageAsync(TestLazySocketMessage.CreateMentionedMessage("--version --foo"), botCurrentUserId);
            AssertEx.ExactlyOneCaution(testObserver.Messages);
        }

        [TestMethod]
        public async Task ScanStart_NoMentionTest()
        {
            ulong botCurrentUserId = TestLazySocketUser.MyBot.Id;
            var (allCommands, testObserver, _) = Init();

            await allCommands.ReceiveMessageAsync(TestLazySocketMessage.CreateNoMentionMessage("scan-start"), botCurrentUserId);
            AssertEx.IsEmpty(testObserver.Messages);
        }

        [TestMethod]
        public async Task ScanEnd_NoMentionTest()
        {
            ulong botCurrentUserId = TestLazySocketUser.MyBot.Id;
            var (allCommands, testObserver, _) = Init();

            await allCommands.ReceiveMessageAsync(TestLazySocketMessage.CreateNoMentionMessage("scan-end"), botCurrentUserId);
            AssertEx.IsEmpty(testObserver.Messages);
        }

        [TestMethod]
        public async Task ScanEnd_DuplicateTest()
        {
            ulong botCurrentUserId = TestLazySocketUser.MyBot.Id;
            var (allCommands, testObserver, _) = Init();

            await allCommands.ReceiveMessageAsync(TestLazySocketMessage.CreateMentionedMessage("scan-start"), botCurrentUserId);
            await allCommands.ReceiveMessageAsync(TestLazySocketMessage.CreateMentionedMessage("scan-end"), botCurrentUserId);
            testObserver.Messages.Clear();

            await allCommands.ReceiveMessageAsync(TestLazySocketMessage.CreateMentionedMessage("scan-end"), botCurrentUserId);
            AssertEx.ExactlyOneCaution(testObserver.Messages);
        }

        [TestMethod]
        public async Task Scan_NoRollTest()
        {
            ulong botCurrentUserId = TestLazySocketUser.MyBot.Id;
            var (allCommands, testObserver, _) = Init();

            await allCommands.ReceiveMessageAsync(TestLazySocketMessage.CreateMentionedMessage("scan-start"), botCurrentUserId);
            AssertEx.ExactlyOneSay(testObserver.Messages);
            testObserver.Messages.Clear();

            await allCommands.ReceiveMessageAsync(TestLazySocketMessage.CreateMentionedMessage("scan-end"), botCurrentUserId);
            AssertEx.ExactlyOneSay(testObserver.Messages);
        }

        [TestMethod]
        public async Task Scan_OneRollTest()
        {
            ulong botCurrentUserId = TestLazySocketUser.MyBot.Id;
            var (allCommands, testObserver, _) = Init();

            await allCommands.ReceiveMessageAsync(TestLazySocketMessage.CreateMentionedMessage("scan-start"), botCurrentUserId);
            AssertEx.ExactlyOneSay(testObserver.Messages);
            testObserver.Messages.Clear();

            await allCommands.ReceiveMessageAsync(TestLazySocketMessage.CreateNoMentionMessage("1d100"), botCurrentUserId);
            AssertEx.AllSay(testObserver.Messages, 2);
            testObserver.Messages.Clear();

            await allCommands.ReceiveMessageAsync(TestLazySocketMessage.CreateMentionedMessage("scan-end"), botCurrentUserId);
            AssertEx.ExactlyOneSay(testObserver.Messages);
        }

        [TestMethod]
        public async Task Scan_TwoRollsTest()
        {
            ulong botCurrentUserId = TestLazySocketUser.MyBot.Id;
            var (allCommands, testObserver, _) = Init();

            await allCommands.ReceiveMessageAsync(TestLazySocketMessage.CreateMentionedMessage("scan-start"), botCurrentUserId);
            AssertEx.ExactlyOneSay(testObserver.Messages);
            testObserver.Messages.Clear();

            await allCommands.ReceiveMessageAsync(TestLazySocketMessage.CreateNoMentionMessage("1d100"), botCurrentUserId);
            AssertEx.AllSay(testObserver.Messages, 2);
            testObserver.Messages.Clear();

            await allCommands.ReceiveMessageAsync(TestLazySocketMessage.CreateNoMentionMessage("1d100"), botCurrentUserId);
            AssertEx.AllSay(testObserver.Messages, 2);
            testObserver.Messages.Clear();

            await allCommands.ReceiveMessageAsync(TestLazySocketMessage.CreateMentionedMessage("scan-end"), botCurrentUserId);
            AssertEx.ExactlyOneSay(testObserver.Messages);
        }

        [TestMethod]
        public async Task Scan_WrongRollTest()
        {
            ulong botCurrentUserId = TestLazySocketUser.MyBot.Id;
            var (allCommands, testObserver, _) = Init();

            await allCommands.ReceiveMessageAsync(TestLazySocketMessage.CreateMentionedMessage("scan-start"), botCurrentUserId);
            AssertEx.ExactlyOneSay(testObserver.Messages);
            testObserver.Messages.Clear();

            await allCommands.ReceiveMessageAsync(TestLazySocketMessage.CreateNoMentionMessage("2d100"), botCurrentUserId);
            AssertEx.ExactlyOneSay(testObserver.Messages);
        }

        [TestMethod]
        public async Task Scan_NonRollTest()
        {
            ulong botCurrentUserId = TestLazySocketUser.MyBot.Id;
            var (allCommands, testObserver, _) = Init();

            await allCommands.ReceiveMessageAsync(TestLazySocketMessage.CreateMentionedMessage("scan-start"), botCurrentUserId);
            AssertEx.ExactlyOneSay(testObserver.Messages);
            testObserver.Messages.Clear();

            await allCommands.ReceiveMessageAsync(TestLazySocketMessage.CreateNoMentionMessage("hooray!"), botCurrentUserId);
            AssertEx.IsEmpty(testObserver.Messages);
        }

        [TestMethod]
        public async Task Scan_DiceOption_OneBigRollTest()
        {
            ulong botCurrentUserId = TestLazySocketUser.MyBot.Id;
            var (allCommands, testObserver, _) = Init();

            await allCommands.ReceiveMessageAsync(TestLazySocketMessage.CreateMentionedMessage("scan-start --dice 100000000000000000000+1d100"), botCurrentUserId);
            AssertEx.ExactlyOneSay(testObserver.Messages);
            testObserver.Messages.Clear();

            await allCommands.ReceiveMessageAsync(TestLazySocketMessage.CreateNoMentionMessage("100000000000000000000+1d100"), botCurrentUserId);
            AssertEx.AllSay(testObserver.Messages, 2);
            testObserver.Messages.Clear();

            await allCommands.ReceiveMessageAsync(TestLazySocketMessage.CreateMentionedMessage("scan-end"), botCurrentUserId);
            AssertEx.ExactlyOneSay(testObserver.Messages);
        }

        [TestMethod]
        public async Task ScanStart_DuplicateTest()
        {
            ulong botCurrentUserId = TestLazySocketUser.MyBot.Id;
            var (allCommands, testObserver, _) = Init();

            await allCommands.ReceiveMessageAsync(TestLazySocketMessage.CreateMentionedMessage("scan-start"), botCurrentUserId);
            testObserver.Messages.Clear();

            await allCommands.ReceiveMessageAsync(TestLazySocketMessage.CreateMentionedMessage("scan-start"), botCurrentUserId);
            AssertEx.ExactlyOneCaution(testObserver.Messages);
            testObserver.Messages.Clear();

            await allCommands.ReceiveMessageAsync(TestLazySocketMessage.CreateMentionedMessage("scan-end"), botCurrentUserId);
            AssertEx.ExactlyOneSay(testObserver.Messages);
        }

        [TestMethod]
        public async Task ScanStart_TimeLimitTest()
        {
            ulong botCurrentUserId = TestLazySocketUser.MyBot.Id;
            var (allCommands, testObserver, time) = Init();

            await allCommands.ReceiveMessageAsync(TestLazySocketMessage.CreateMentionedMessage("scan-start"), botCurrentUserId);
            testObserver.Messages.Clear();

            time.AdvanceBy(TimeSpan.FromHours(1.5)); // TimeLimit になるくらい長時間経過させる
            await Task.Delay(time.IntervalOfUpdatingScans + TimeSpan.FromSeconds(1)); // 必ず IntervalOfUpdatingScans 以上の時間が経過するよう +1秒 している
            await allCommands.ReceiveMessageAsync(TestLazySocketMessage.CreateMentionedMessage("scan-end"), botCurrentUserId);
            AssertEx.TypeCountsAre(testObserver.Messages, sayCount:1, cautionCount: 1);
            testObserver.Messages.Clear();

            await allCommands.ReceiveMessageAsync(TestLazySocketMessage.CreateMentionedMessage("scan-start"), botCurrentUserId);
            AssertEx.ExactlyOneSay(testObserver.Messages);
        }

        [TestMethod]
        public async Task ScanShowTest()
        {
            var botCurrentUserId = TestLazySocketUser.MyBot.Id;
            var (allCommands, testObserver, _) = Init();

            await allCommands.ReceiveMessageAsync(TestLazySocketMessage.CreateMentionedMessage("scan-start"), botCurrentUserId);
            await allCommands.ReceiveMessageAsync(TestLazySocketMessage.CreateMentionedMessage("1d100"), botCurrentUserId);
            testObserver.Messages.Clear();

            await allCommands.ReceiveMessageAsync(TestLazySocketMessage.CreateMentionedMessage("scan-show"), botCurrentUserId);
            AssertEx.ExactlyOneSay(testObserver.Messages);
        }

        [TestMethod]
        public async Task ScanShow_CacheTest()
        {
            var botCurrentUserId = TestLazySocketUser.MyBot.Id;
            var (allCommands, testObserver, time) = Init();

            await allCommands.ReceiveMessageAsync(TestLazySocketMessage.CreateMentionedMessage("scan-start"), botCurrentUserId);
            await allCommands.ReceiveMessageAsync(TestLazySocketMessage.CreateMentionedMessage("1d100"), botCurrentUserId);
            time.AdvanceBy(TimeSpan.FromHours(1.5)); // TimeLimit になるくらい長時間経過させる
            await Task.Delay(time.IntervalOfUpdatingScans + TimeSpan.FromSeconds(1)); // 必ず IntervalOfUpdatingScans 以上の時間が経過するよう +1秒 している
            await allCommands.ReceiveMessageAsync(TestLazySocketMessage.CreateMentionedMessage("scan-end"), botCurrentUserId);
            testObserver.Messages.Clear();

            await allCommands.ReceiveMessageAsync(TestLazySocketMessage.CreateMentionedMessage("scan-show"), botCurrentUserId);
            AssertEx.ExactlyOneSay(testObserver.Messages);
        }

        [TestMethod]
        public async Task ScanShow_NoCacheTest()
        {
            var botCurrentUserId = TestLazySocketUser.MyBot.Id;
            var (allCommands, testObserver, time) = Init();

            await allCommands.ReceiveMessageAsync(TestLazySocketMessage.CreateMentionedMessage("scan-start"), botCurrentUserId);
            await allCommands.ReceiveMessageAsync(TestLazySocketMessage.CreateMentionedMessage("1d100"), botCurrentUserId);
            time.AdvanceBy(TimeSpan.FromHours(1.5)); // TimeLimit になるくらい長時間経過させる
            await allCommands.ReceiveMessageAsync(TestLazySocketMessage.CreateMentionedMessage("scan-end"), botCurrentUserId);
            time.AdvanceBy(TimeSpan.FromHours(1.5)); // キャッシュが削除されるくらい長時間経過させる
            await Task.Delay(time.IntervalOfUpdatingScans + TimeSpan.FromSeconds(1)); // 必ず IntervalOfUpdatingScans 以上の時間が経過するよう +1秒 している

            testObserver.Messages.Clear();

            await allCommands.ReceiveMessageAsync(TestLazySocketMessage.CreateMentionedMessage("scan-show"), botCurrentUserId);
            AssertEx.ExactlyOneCaution(testObserver.Messages);
        }
    }
}
