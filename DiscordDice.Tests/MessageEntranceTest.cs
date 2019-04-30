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
                    expected.Where(ResponseFilter()),
                    actual.Where(ResponseFilter()));
            }

            public static void TypesAre(IEnumerable<Recorded<Notification<Response>>> actual, params ResponseType[] expected)
            {
                var actual_ =
                    actual
                    .Where(ResponseFilter())
                    .Select(r => r.Value.Value.Type)
                    .ToArray();
                CollectionAssert.AreEqual(expected, actual_);
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
                CollectionAssert.AreEquivalent(expected, actual_);
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
            var texts = new[] { "hello", "a a", "a\r\na" };
            foreach(var text in texts)
            {
                await Plain_NoMentionTestCore(text);
            }
        }

        async Task Plain_NoMentionTestCore(string text)
        {
            ulong botCurrentUserId = TestLazySocketUser.MyBot.Id;
            var (allCommands, testObserver, _) = Init();

            await allCommands.ReceiveMessageAsync(TestLazySocketMessage.CreateNoMentionMessage(text), botCurrentUserId);
            AssertEx.IsEmpty(testObserver.Messages);
        }

        [TestMethod]
        public async Task Plain_NotMentionedTest()
        {
            var texts = new[] { "hello", "a a", "a\r\na" };
            foreach (var text in texts)
            {
                await Plain_NotMentionedTestCore(text);
            }
        }

        async Task Plain_NotMentionedTestCore(string text)
        {
            ulong botCurrentUserId = TestLazySocketUser.MyBot.Id;
            var (allCommands, testObserver, _) = Init();

            await allCommands.ReceiveMessageAsync(TestLazySocketMessage.CreateNotMentionedMessage(text), botCurrentUserId);
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
        public async Task Roll_0DTest()
        {
            ulong botCurrentUserId = TestLazySocketUser.MyBot.Id;
            var (allCommands, testObserver, _) = Init();

            await allCommands.ReceiveMessageAsync(TestLazySocketMessage.CreateNoMentionMessage("0d100"), botCurrentUserId);
            AssertEx.ExactlyOneSay(testObserver.Messages);
        }

        [TestMethod]
        public async Task Roll_D0Test()
        {
            ulong botCurrentUserId = TestLazySocketUser.MyBot.Id;
            var (allCommands, testObserver, _) = Init();

            await allCommands.ReceiveMessageAsync(TestLazySocketMessage.CreateNoMentionMessage("1d0"), botCurrentUserId);
            AssertEx.ExactlyOneSay(testObserver.Messages);
        }

        [TestMethod]
        public async Task Roll_0D0Test()
        {
            ulong botCurrentUserId = TestLazySocketUser.MyBot.Id;
            var (allCommands, testObserver, _) = Init();

            await allCommands.ReceiveMessageAsync(TestLazySocketMessage.CreateNoMentionMessage("0d0"), botCurrentUserId);
            AssertEx.ExactlyOneSay(testObserver.Messages);
        }

        [TestMethod]
        public async Task Roll_PlusTest()
        {
            ulong botCurrentUserId = TestLazySocketUser.MyBot.Id;
            var (allCommands, testObserver, _) = Init();

            await allCommands.ReceiveMessageAsync(TestLazySocketMessage.CreateNoMentionMessage("+1d100"), botCurrentUserId);
            AssertEx.ExactlyOneSay(testObserver.Messages);
        }

        [TestMethod]
        public async Task Roll_MinusTest()
        {
            ulong botCurrentUserId = TestLazySocketUser.MyBot.Id;
            var (allCommands, testObserver, _) = Init();

            await allCommands.ReceiveMessageAsync(TestLazySocketMessage.CreateNoMentionMessage("-1d100"), botCurrentUserId);
            AssertEx.ExactlyOneSay(testObserver.Messages);
        }

        [TestMethod]
        public async Task Roll_ExpressionTest()
        {
            ulong botCurrentUserId = TestLazySocketUser.MyBot.Id;
            var (allCommands, testObserver, _) = Init();

            await allCommands.ReceiveMessageAsync(TestLazySocketMessage.CreateNoMentionMessage("-1d4-4+2d100"), botCurrentUserId);
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
        public async Task LegacyHelp_MentionedTest()
        {
            ulong botCurrentUserId = TestLazySocketUser.MyBot.Id;
            var (allCommands, testObserver, _) = Init();

            await allCommands.ReceiveMessageAsync(TestLazySocketMessage.CreateMentionedMessage("help"), botCurrentUserId);
            AssertEx.ExactlyOneSay(testObserver.Messages);
        }

        [TestMethod]
        public async Task LegacyHelp_NoMentionTest()
        {
            ulong botCurrentUserId = TestLazySocketUser.MyBot.Id;
            var (allCommands, testObserver, _) = Init();

            await allCommands.ReceiveMessageAsync(TestLazySocketMessage.CreateNoMentionMessage("help"), botCurrentUserId);
            AssertEx.IsEmpty(testObserver.Messages);
        }

        [TestMethod]
        public async Task LegacyHelp_WithOptionTest()
        {
            ulong botCurrentUserId = TestLazySocketUser.MyBot.Id;
            var (allCommands, testObserver, _) = Init();

            await allCommands.ReceiveMessageAsync(TestLazySocketMessage.CreateMentionedMessage("help --foo"), botCurrentUserId);
            AssertEx.ExactlyOneCaution(testObserver.Messages);
        }


        [TestMethod]
        public async Task LegacyHWithHyphen_MentionedTest()
        {
            ulong botCurrentUserId = TestLazySocketUser.MyBot.Id;
            var (allCommands, testObserver, _) = Init();

            await allCommands.ReceiveMessageAsync(TestLazySocketMessage.CreateMentionedMessage("-h"), botCurrentUserId);
            AssertEx.ExactlyOneSay(testObserver.Messages);
        }

        [TestMethod]
        public async Task LegacyHWithHyphen_NoMentionTest()
        {
            ulong botCurrentUserId = TestLazySocketUser.MyBot.Id;
            var (allCommands, testObserver, _) = Init();

            await allCommands.ReceiveMessageAsync(TestLazySocketMessage.CreateNoMentionMessage("-h"), botCurrentUserId);
            AssertEx.IsEmpty(testObserver.Messages);
        }

        [TestMethod]
        public async Task LegacyHWithHyphen_WithOptionTest()
        {
            ulong botCurrentUserId = TestLazySocketUser.MyBot.Id;
            var (allCommands, testObserver, _) = Init();

            await allCommands.ReceiveMessageAsync(TestLazySocketMessage.CreateMentionedMessage("-h --foo"), botCurrentUserId);
            AssertEx.ExactlyOneCaution(testObserver.Messages);
        }

        [TestMethod]
        public async Task LegacyHelpWithHyphen_MentionedTest()
        {
            ulong botCurrentUserId = TestLazySocketUser.MyBot.Id;
            var (allCommands, testObserver, _) = Init();

            await allCommands.ReceiveMessageAsync(TestLazySocketMessage.CreateMentionedMessage("--help"), botCurrentUserId);
            AssertEx.ExactlyOneSay(testObserver.Messages);
        }

        [TestMethod]
        public async Task LegacyHelpWithHyphen_NoMentionTest()
        {
            ulong botCurrentUserId = TestLazySocketUser.MyBot.Id;
            var (allCommands, testObserver, _) = Init();

            await allCommands.ReceiveMessageAsync(TestLazySocketMessage.CreateNoMentionMessage("--help"), botCurrentUserId);
            AssertEx.IsEmpty(testObserver.Messages);
        }

        [TestMethod]
        public async Task LegacyHelpWithHyphen_WithOptionTest()
        {
            ulong botCurrentUserId = TestLazySocketUser.MyBot.Id;
            var (allCommands, testObserver, _) = Init();

            await allCommands.ReceiveMessageAsync(TestLazySocketMessage.CreateMentionedMessage("--help --foo"), botCurrentUserId);
            AssertEx.ExactlyOneCaution(testObserver.Messages);
        }

        [TestMethod]
        public async Task LegacyVersion_MentionedTest()
        {
            ulong botCurrentUserId = TestLazySocketUser.MyBot.Id;
            var (allCommands, testObserver, _) = Init();

            await allCommands.ReceiveMessageAsync(TestLazySocketMessage.CreateMentionedMessage("version"), botCurrentUserId);
            AssertEx.ExactlyOneSay(testObserver.Messages);
        }

        [TestMethod]
        public async Task LegacyVersion_NoMentionTest()
        {
            ulong botCurrentUserId = TestLazySocketUser.MyBot.Id;
            var (allCommands, testObserver, _) = Init();

            await allCommands.ReceiveMessageAsync(TestLazySocketMessage.CreateNoMentionMessage("version"), botCurrentUserId);
            AssertEx.IsEmpty(testObserver.Messages);
        }

        [TestMethod]
        public async Task LegacyVersion_WithOptionTest()
        {
            ulong botCurrentUserId = TestLazySocketUser.MyBot.Id;
            var (allCommands, testObserver, _) = Init();

            await allCommands.ReceiveMessageAsync(TestLazySocketMessage.CreateMentionedMessage("version --foo"), botCurrentUserId);
            AssertEx.ExactlyOneCaution(testObserver.Messages);
        }

        [TestMethod]
        public async Task LegacyVWithHyphen_MentionedTest()
        {
            ulong botCurrentUserId = TestLazySocketUser.MyBot.Id;
            var (allCommands, testObserver, _) = Init();

            await allCommands.ReceiveMessageAsync(TestLazySocketMessage.CreateMentionedMessage("-v"), botCurrentUserId);
            AssertEx.ExactlyOneSay(testObserver.Messages);
        }

        [TestMethod]
        public async Task LegacyVWithHyphen_NoMentionTest()
        {
            ulong botCurrentUserId = TestLazySocketUser.MyBot.Id;
            var (allCommands, testObserver, _) = Init();

            await allCommands.ReceiveMessageAsync(TestLazySocketMessage.CreateNoMentionMessage("-v"), botCurrentUserId);
            AssertEx.IsEmpty(testObserver.Messages);
        }

        [TestMethod]
        public async Task LegacyVWithHyphen_WithOptionTest()
        {
            ulong botCurrentUserId = TestLazySocketUser.MyBot.Id;
            var (allCommands, testObserver, _) = Init();

            await allCommands.ReceiveMessageAsync(TestLazySocketMessage.CreateMentionedMessage("-v --foo"), botCurrentUserId);
            AssertEx.ExactlyOneCaution(testObserver.Messages);
        }


        [TestMethod]
        public async Task LegacyVersionWithHyphen_MentionedTest()
        {
            ulong botCurrentUserId = TestLazySocketUser.MyBot.Id;
            var (allCommands, testObserver, _) = Init();

            await allCommands.ReceiveMessageAsync(TestLazySocketMessage.CreateMentionedMessage("--version"), botCurrentUserId);
            AssertEx.ExactlyOneSay(testObserver.Messages);
        }

        [TestMethod]
        public async Task LegacyVersionWithHyphen_NoMentionTest()
        {
            ulong botCurrentUserId = TestLazySocketUser.MyBot.Id;
            var (allCommands, testObserver, _) = Init();

            await allCommands.ReceiveMessageAsync(TestLazySocketMessage.CreateNoMentionMessage("--version"), botCurrentUserId);
            AssertEx.IsEmpty(testObserver.Messages);
        }

        [TestMethod]
        public async Task LegacyVersionWithHyphen_WithOptionTest()
        {
            ulong botCurrentUserId = TestLazySocketUser.MyBot.Id;
            var (allCommands, testObserver, _) = Init();

            await allCommands.ReceiveMessageAsync(TestLazySocketMessage.CreateMentionedMessage("--version --foo"), botCurrentUserId);
            AssertEx.ExactlyOneCaution(testObserver.Messages);
        }

        [TestMethod]
        public async Task LegacyScanStart_NoMentionTest()
        {
            ulong botCurrentUserId = TestLazySocketUser.MyBot.Id;
            var (allCommands, testObserver, _) = Init();

            await allCommands.ReceiveMessageAsync(TestLazySocketMessage.CreateNoMentionMessage("scan-start"), botCurrentUserId);
            AssertEx.IsEmpty(testObserver.Messages);
        }

        [TestMethod]
        public async Task LegacyScanEnd_NoMentionTest()
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

            var message0 = TestLazySocketMessage.CreateNoMentionMessage("!scan-start");
            var message1 = TestLazySocketMessage.CreateNoMentionMessage("!scan-end");
            var message2 = TestLazySocketMessage.CreateNoMentionMessage("!scan-end");
            await ScanEnd_DuplicateTestCore(message0, message1, message2);
        }

        [TestMethod]
        public async Task LegacyScanEnd_DuplicateTest()
        {
            ulong botCurrentUserId = TestLazySocketUser.MyBot.Id;
            var (allCommands, testObserver, _) = Init();

            var message0 = TestLazySocketMessage.CreateMentionedMessage("scan-start");
            var message1 = TestLazySocketMessage.CreateMentionedMessage("scan-end");
            var message2 = TestLazySocketMessage.CreateMentionedMessage("scan-end");
            await ScanEnd_DuplicateTestCore(message0, message1, message2);
        }

        async Task ScanEnd_DuplicateTestCore(ILazySocketMessage message0, ILazySocketMessage message1, ILazySocketMessage message2)
        {
            ulong botCurrentUserId = TestLazySocketUser.MyBot.Id;
            var (allCommands, testObserver, _) = Init();

            await allCommands.ReceiveMessageAsync(message0, botCurrentUserId);
            await allCommands.ReceiveMessageAsync(message1, botCurrentUserId);
            testObserver.Messages.Clear();

            await allCommands.ReceiveMessageAsync(message2, botCurrentUserId);
            AssertEx.ExactlyOneCaution(testObserver.Messages);
        }

        [TestMethod]
        public async Task Scan_NoRollTest()
        {
            var message0 = TestLazySocketMessage.CreateNoMentionMessage("!scan-start");
            var message1 = TestLazySocketMessage.CreateNoMentionMessage("!scan-end");
            await Scan_NoRollTestCore(message0, message1);
        }

        [TestMethod]
        public async Task LegacyScan_NoRollTest()
        {
            var message0 = TestLazySocketMessage.CreateMentionedMessage("scan-start");
            var message1 = TestLazySocketMessage.CreateMentionedMessage("scan-end");
            await Scan_NoRollTestCore(message0, message1);
        }

        async Task Scan_NoRollTestCore(ILazySocketMessage message0, ILazySocketMessage message1)
        {
            ulong botCurrentUserId = TestLazySocketUser.MyBot.Id;
            var (allCommands, testObserver, _) = Init();

            await allCommands.ReceiveMessageAsync(message0, botCurrentUserId);
            AssertEx.ExactlyOneSay(testObserver.Messages);
            testObserver.Messages.Clear();

            await allCommands.ReceiveMessageAsync(message1, botCurrentUserId);
            AssertEx.ExactlyOneSay(testObserver.Messages);
        }

        [TestMethod]
        public async Task Scan_OneRollTest()
        {
            var message0 = TestLazySocketMessage.CreateNoMentionMessage("!scan-start");
            var message1 = TestLazySocketMessage.CreateNoMentionMessage("1d100");
            var message2 = TestLazySocketMessage.CreateNoMentionMessage("!scan-end");
            await Scan_OneRollTestCore(message0, message1, message2);
        }

        [TestMethod]
        public async Task LegacyScan_OneRollTest()
        {
            var message0 = TestLazySocketMessage.CreateMentionedMessage("scan-start");
            var message1 = TestLazySocketMessage.CreateNoMentionMessage("1d100");
            var message2 = TestLazySocketMessage.CreateMentionedMessage("scan-end");
            await Scan_OneRollTestCore(message0, message1, message2);
        }

        async Task Scan_OneRollTestCore(ILazySocketMessage message0, ILazySocketMessage message1, ILazySocketMessage message2)
        {
            ulong botCurrentUserId = TestLazySocketUser.MyBot.Id;
            var (allCommands, testObserver, _) = Init();

            await allCommands.ReceiveMessageAsync(message0, botCurrentUserId);
            AssertEx.ExactlyOneSay(testObserver.Messages);
            testObserver.Messages.Clear();

            await allCommands.ReceiveMessageAsync(message1, botCurrentUserId);
            AssertEx.AllSay(testObserver.Messages, 2);
            testObserver.Messages.Clear();

            await allCommands.ReceiveMessageAsync(message2, botCurrentUserId);
            AssertEx.ExactlyOneSay(testObserver.Messages);
        }

        [TestMethod]
        public async Task Scan_TwoRollsTest()
        {
            var message0 = TestLazySocketMessage.CreateNoMentionMessage("!scan-start");
            var message1 = TestLazySocketMessage.CreateNoMentionMessage("1d100");
            var message2 = TestLazySocketMessage.CreateNoMentionMessage("1d100");
            var message3 = TestLazySocketMessage.CreateNoMentionMessage("!scan-end");

            await Scan_TwoRollsTestCore(message0, message1, message2, message3);
        }

        [TestMethod]
        public async Task LegacyScan_TwoRollsTest()
        {
            var message0 = TestLazySocketMessage.CreateMentionedMessage("scan-start");
            var message1 = TestLazySocketMessage.CreateNoMentionMessage("1d100");
            var message2 = TestLazySocketMessage.CreateNoMentionMessage("1d100");
            var message3 = TestLazySocketMessage.CreateMentionedMessage("scan-end");

            await Scan_TwoRollsTestCore(message0, message1, message2, message3);
        }

        async Task Scan_TwoRollsTestCore(TestLazySocketMessage message0, TestLazySocketMessage message1, TestLazySocketMessage message2, TestLazySocketMessage message3)
        {
            ulong botCurrentUserId = TestLazySocketUser.MyBot.Id;
            var (allCommands, testObserver, _) = Init();

            await allCommands.ReceiveMessageAsync(message0, botCurrentUserId);
            AssertEx.ExactlyOneSay(testObserver.Messages);
            testObserver.Messages.Clear();
   
            await allCommands.ReceiveMessageAsync(message1, botCurrentUserId);
            AssertEx.AllSay(testObserver.Messages, 2);
            testObserver.Messages.Clear();
       
            await allCommands.ReceiveMessageAsync(message2, botCurrentUserId);
            AssertEx.AllSay(testObserver.Messages, 2);
            testObserver.Messages.Clear();
      
            await allCommands.ReceiveMessageAsync(message3, botCurrentUserId);
            AssertEx.ExactlyOneSay(testObserver.Messages);
        }

        [TestMethod]
        public async Task Scan_WrongRollTest()
        {
            var message0 = TestLazySocketMessage.CreateNoMentionMessage("!scan-start");
            var message1 = TestLazySocketMessage.CreateNoMentionMessage("2d100");

            await Scan_WrongRollTestCore(message0, message1);
        }

        [TestMethod]
        public async Task LegacyScan_WrongRollTest()
        {
            var message0 = TestLazySocketMessage.CreateMentionedMessage("scan-start");
            var message1 = TestLazySocketMessage.CreateNoMentionMessage("2d100");

            await Scan_WrongRollTestCore(message0, message1);
        }

        async Task Scan_WrongRollTestCore(TestLazySocketMessage message0, TestLazySocketMessage message1)
        {
            ulong botCurrentUserId = TestLazySocketUser.MyBot.Id;
            var (allCommands, testObserver, _) = Init();

            await allCommands.ReceiveMessageAsync(message0, botCurrentUserId);
            AssertEx.ExactlyOneSay(testObserver.Messages);
            testObserver.Messages.Clear();

            await allCommands.ReceiveMessageAsync(message1, botCurrentUserId);
            AssertEx.ExactlyOneSay(testObserver.Messages);
        }

        [TestMethod]
        public async Task Scan_NonRollTest()
        {
            var message0 = TestLazySocketMessage.CreateNoMentionMessage("!scan-start");
            var message1 = TestLazySocketMessage.CreateNoMentionMessage("hooray!");

            await Scan_NonRollTestCore(message0, message1);
        }

        [TestMethod]
        public async Task LegacyScan_NonRollTest()
        {
            var message0 = TestLazySocketMessage.CreateMentionedMessage("scan-start");
            var message1 = TestLazySocketMessage.CreateNoMentionMessage("hooray!");

            await Scan_NonRollTestCore(message0, message1);
        }

        async Task Scan_NonRollTestCore(TestLazySocketMessage message0, TestLazySocketMessage message1)
        {
            ulong botCurrentUserId = TestLazySocketUser.MyBot.Id;
            var (allCommands, testObserver, _) = Init();

            await allCommands.ReceiveMessageAsync(message0, botCurrentUserId);
            AssertEx.ExactlyOneSay(testObserver.Messages);
            testObserver.Messages.Clear();

            await allCommands.ReceiveMessageAsync(message1, botCurrentUserId);
            AssertEx.IsEmpty(testObserver.Messages);
        }

        [TestMethod]
        public async Task Scan_DiceOption_OneBigRollTest()
        {
            TestLazySocketMessage message0 = TestLazySocketMessage.CreateNoMentionMessage("!scan-start --dice 100000000000000000000+1d100");
            TestLazySocketMessage message1 = TestLazySocketMessage.CreateNoMentionMessage("100000000000000000000+1d100");
            TestLazySocketMessage message2 = TestLazySocketMessage.CreateNoMentionMessage("!scan-end");

            await Scan_DiceOption_OneBigRollTestCore(message0, message1, message2);
        }

        [TestMethod]
        public async Task LegacyScan_DiceOption_OneBigRollTest()
        {
            TestLazySocketMessage message0 = TestLazySocketMessage.CreateMentionedMessage("scan-start --dice 100000000000000000000+1d100");
            TestLazySocketMessage message1 = TestLazySocketMessage.CreateNoMentionMessage("100000000000000000000+1d100");
            TestLazySocketMessage message2 = TestLazySocketMessage.CreateMentionedMessage("scan-end");

            await Scan_DiceOption_OneBigRollTestCore(message0, message1, message2);
        }

        async Task Scan_DiceOption_OneBigRollTestCore(TestLazySocketMessage message0, TestLazySocketMessage message1, TestLazySocketMessage message2)
        {
            ulong botCurrentUserId = TestLazySocketUser.MyBot.Id;
            var (allCommands, testObserver, _) = Init();

            await allCommands.ReceiveMessageAsync(message0, botCurrentUserId);
            AssertEx.ExactlyOneSay(testObserver.Messages);
            testObserver.Messages.Clear();

            await allCommands.ReceiveMessageAsync(message1, botCurrentUserId);
            AssertEx.AllSay(testObserver.Messages, 2);
            testObserver.Messages.Clear();

            await allCommands.ReceiveMessageAsync(message2, botCurrentUserId);
            AssertEx.ExactlyOneSay(testObserver.Messages);
        }

        [TestMethod]
        public async Task Scan_TwoUsersTest()
        {
            var message0 = TestLazySocketMessage.CreateNoMentionMessage("!scan-start", TestLazySocketUser.Author);
            var message1 = TestLazySocketMessage.CreateNoMentionMessage("!scan-start", TestLazySocketUser.NonAuthor);
            var message2 = TestLazySocketMessage.CreateNoMentionMessage("!scan-end", TestLazySocketUser.Author);
            var message3 = TestLazySocketMessage.CreateNoMentionMessage("!scan-end", TestLazySocketUser.NonAuthor);

            await Scan_TwoUsersTestCore(message0, message1, message2, message3);
        }

        [TestMethod]
        public async Task LegacyScan_TwoUsersTest()
        {
            var message0 = TestLazySocketMessage.CreateMentionedMessage("scan-start", TestLazySocketUser.Author);
            var message1 = TestLazySocketMessage.CreateMentionedMessage("scan-start", TestLazySocketUser.NonAuthor);
            var message2 = TestLazySocketMessage.CreateMentionedMessage("scan-end", TestLazySocketUser.Author);
            var message3 = TestLazySocketMessage.CreateMentionedMessage("scan-end", TestLazySocketUser.NonAuthor);

            await Scan_TwoUsersTestCore(message0, message1, message2, message3);
        }

        async Task Scan_TwoUsersTestCore(TestLazySocketMessage message0, TestLazySocketMessage message1, TestLazySocketMessage message2, TestLazySocketMessage message3)
        {
            ulong botCurrentUserId = TestLazySocketUser.MyBot.Id;
            var (allCommands, testObserver, _) = Init();
            
            await allCommands.ReceiveMessageAsync(message0, botCurrentUserId);
            AssertEx.ExactlyOneSay(testObserver.Messages);
            testObserver.Messages.Clear();
           
            await allCommands.ReceiveMessageAsync(message1, botCurrentUserId);
            AssertEx.ExactlyOneSay(testObserver.Messages);
            testObserver.Messages.Clear();
            
            await allCommands.ReceiveMessageAsync(message2, botCurrentUserId);
            AssertEx.ExactlyOneSay(testObserver.Messages);
            testObserver.Messages.Clear();

            await allCommands.ReceiveMessageAsync(message3, botCurrentUserId);
            AssertEx.ExactlyOneSay(testObserver.Messages);
        }

        [TestMethod]
        public async Task ScanStart_DuplicateTest()
        {
            var message0 = TestLazySocketMessage.CreateNoMentionMessage("!scan-start");
            var message1 = TestLazySocketMessage.CreateNoMentionMessage("!scan-start");
            var message2 = TestLazySocketMessage.CreateNoMentionMessage("!scan-end");

            await Scan_TwoUsersTestCore(message0, message1, message2);
        }

        [TestMethod]
        public async Task LegacyScanStart_DuplicateTest()
        {
            var message0 = TestLazySocketMessage.CreateMentionedMessage("scan-start");
            var message1 = TestLazySocketMessage.CreateMentionedMessage("scan-start");
            var message2 = TestLazySocketMessage.CreateMentionedMessage("scan-end");

            await Scan_TwoUsersTestCore(message0, message1, message2);
        }

        async Task Scan_TwoUsersTestCore(TestLazySocketMessage message0, TestLazySocketMessage message1, TestLazySocketMessage message2)
        {
            ulong botCurrentUserId = TestLazySocketUser.MyBot.Id;
            var (allCommands, testObserver, _) = Init();
            
            await allCommands.ReceiveMessageAsync(message0, botCurrentUserId);
            testObserver.Messages.Clear();
            
            await allCommands.ReceiveMessageAsync(message1, botCurrentUserId);
            AssertEx.ExactlyOneCaution(testObserver.Messages);
            testObserver.Messages.Clear();
            
            await allCommands.ReceiveMessageAsync(message2, botCurrentUserId);
            AssertEx.ExactlyOneSay(testObserver.Messages);
        }

        [TestMethod]
        public async Task ScanStart_TimeLimitTest()
        {
            TestLazySocketMessage message0 = TestLazySocketMessage.CreateNoMentionMessage("!scan-start");
            TestLazySocketMessage message1 = TestLazySocketMessage.CreateNoMentionMessage("!scan-end");
            TestLazySocketMessage message2 = TestLazySocketMessage.CreateNoMentionMessage("!scan-start");

            await ScanStart_TimeLimitTestCore(message0, message1, message2);
        }

        [TestMethod]
        public async Task LegacyScanStart_TimeLimitTest()
        {
            TestLazySocketMessage message0 = TestLazySocketMessage.CreateMentionedMessage("scan-start");
            TestLazySocketMessage message1 = TestLazySocketMessage.CreateMentionedMessage("scan-end");
            TestLazySocketMessage message2 = TestLazySocketMessage.CreateMentionedMessage("scan-start");

            await ScanStart_TimeLimitTestCore(message0, message1, message2);
        }

        async Task ScanStart_TimeLimitTestCore(TestLazySocketMessage message0, TestLazySocketMessage message1, TestLazySocketMessage message2)
        {
            ulong botCurrentUserId = TestLazySocketUser.MyBot.Id;
            var (allCommands, testObserver, time) = Init();

            await allCommands.ReceiveMessageAsync(message0, botCurrentUserId);
            testObserver.Messages.Clear();

            time.AdvanceBy(TimeSpan.FromHours(1.5)); // TimeLimit になるくらい長時間経過させる
            await Task.Delay(time.IntervalOfUpdatingScans + TimeSpan.FromSeconds(1)); // 必ず IntervalOfUpdatingScans 以上の時間が経過するよう +1秒 している
            await allCommands.ReceiveMessageAsync(message1, botCurrentUserId);
            AssertEx.TypeCountsAre(testObserver.Messages, sayCount:1, cautionCount: 1);
            testObserver.Messages.Clear();

            await allCommands.ReceiveMessageAsync(message2, botCurrentUserId);
            AssertEx.ExactlyOneSay(testObserver.Messages);
        }

        [TestMethod]
        public async Task ScanShowTest()
        {
            var message0 = TestLazySocketMessage.CreateNoMentionMessage("!scan-start");
            var message1 = TestLazySocketMessage.CreateNoMentionMessage("1d100");
            var message2 = TestLazySocketMessage.CreateNoMentionMessage("!scan-show");

            await ScanShowTestCore(message0, message1, message2);
        }

        [TestMethod]
        public async Task LegacyScanShowTest()
        {
            var message0 = TestLazySocketMessage.CreateMentionedMessage("scan-start");
            var message1 = TestLazySocketMessage.CreateNoMentionMessage("1d100");
            var message2 = TestLazySocketMessage.CreateMentionedMessage("scan-show");

            await ScanShowTestCore(message0, message1, message2);
        }

        async Task ScanShowTestCore(TestLazySocketMessage message0, TestLazySocketMessage message1, TestLazySocketMessage message2)
        {
            var botCurrentUserId = TestLazySocketUser.MyBot.Id;
            var (allCommands, testObserver, _) = Init();

            await allCommands.ReceiveMessageAsync(message0, botCurrentUserId);
            await allCommands.ReceiveMessageAsync(message1, botCurrentUserId);
            testObserver.Messages.Clear();

            await allCommands.ReceiveMessageAsync(message2, botCurrentUserId);
            AssertEx.ExactlyOneSay(testObserver.Messages);
        }

        [TestMethod]
        public async Task ScanShow_CacheTest()
        {
            TestLazySocketMessage message0 = TestLazySocketMessage.CreateNoMentionMessage("!scan-start");
            TestLazySocketMessage message1 = TestLazySocketMessage.CreateNoMentionMessage("!1d100");
            TestLazySocketMessage message2 = TestLazySocketMessage.CreateNoMentionMessage("!scan-end");
            TestLazySocketMessage message3 = TestLazySocketMessage.CreateNoMentionMessage("!scan-show");

            await ScanShow_CacheTestCore(message0, message1, message2, message3);
        }

        [TestMethod]
        public async Task LegacyScanShow_CacheTest()
        {
            TestLazySocketMessage message0 = TestLazySocketMessage.CreateMentionedMessage("scan-start");
            TestLazySocketMessage message1 = TestLazySocketMessage.CreateNoMentionMessage("1d100");
            TestLazySocketMessage message2 = TestLazySocketMessage.CreateMentionedMessage("scan-end");
            TestLazySocketMessage message3 = TestLazySocketMessage.CreateMentionedMessage("scan-show");

            await ScanShow_CacheTestCore(message0, message1, message2, message3);
        }

        async Task ScanShow_CacheTestCore(TestLazySocketMessage message0, TestLazySocketMessage message1, TestLazySocketMessage message2, TestLazySocketMessage message3)
        {
            var botCurrentUserId = TestLazySocketUser.MyBot.Id;
            var (allCommands, testObserver, time) = Init();
            
            await allCommands.ReceiveMessageAsync(message0, botCurrentUserId);   
            await allCommands.ReceiveMessageAsync(message1, botCurrentUserId);
            time.AdvanceBy(TimeSpan.FromHours(1.5)); // TimeLimit になるくらい長時間経過させる
            await Task.Delay(time.IntervalOfUpdatingScans + TimeSpan.FromSeconds(1)); // 必ず IntervalOfUpdatingScans 以上の時間が経過するよう +1秒 している
            await allCommands.ReceiveMessageAsync(message2, botCurrentUserId);
            testObserver.Messages.Clear();
          
            await allCommands.ReceiveMessageAsync(message3, botCurrentUserId);
            AssertEx.ExactlyOneSay(testObserver.Messages);
        }

        [TestMethod]
        public async Task ScanShow_NoCacheTest()
        {
            var message0 = TestLazySocketMessage.CreateNoMentionMessage("!scan-start");
            var message1 = TestLazySocketMessage.CreateNoMentionMessage("1d100");
            var message2 = TestLazySocketMessage.CreateNoMentionMessage("!scan-end");
            var message3 = TestLazySocketMessage.CreateNoMentionMessage("!scan-show");

            await ScanShow_NoCacheTestCore(message0, message1, message2, message3);
        }

        [TestMethod]
        public async Task LegacyScanShow_NoCacheTest()
        {
            var message0 = TestLazySocketMessage.CreateMentionedMessage("scan-start");
            var message1 = TestLazySocketMessage.CreateNoMentionMessage("1d100");
            var message2 = TestLazySocketMessage.CreateMentionedMessage("scan-end");
            var message3 = TestLazySocketMessage.CreateMentionedMessage("scan-show");

            await ScanShow_NoCacheTestCore(message0, message1, message2, message3);
        }

        async Task ScanShow_NoCacheTestCore(TestLazySocketMessage message0, TestLazySocketMessage message1, TestLazySocketMessage message2, TestLazySocketMessage message3)
        {
            var botCurrentUserId = TestLazySocketUser.MyBot.Id;
            var (allCommands, testObserver, time) = Init();
       
            await allCommands.ReceiveMessageAsync(message0, botCurrentUserId);
            await allCommands.ReceiveMessageAsync(message1, botCurrentUserId);
            time.AdvanceBy(TimeSpan.FromHours(1.5)); // TimeLimit になるくらい長時間経過させる 
            await allCommands.ReceiveMessageAsync(message2, botCurrentUserId);
            time.AdvanceBy(TimeSpan.FromHours(1.5)); // キャッシュが削除されるくらい長時間経過させる
            await Task.Delay(time.IntervalOfUpdatingScans + TimeSpan.FromSeconds(1)); // 必ず IntervalOfUpdatingScans 以上の時間が経過するよう +1秒 している

            testObserver.Messages.Clear();
           
            await allCommands.ReceiveMessageAsync(message3, botCurrentUserId);
            AssertEx.ExactlyOneCaution(testObserver.Messages);
        }
    }
}
