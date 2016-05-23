//
// Copyright (c) Antmicro
// Copyright (c) Realtime Embedded
//
// This file is part of the Emul8 project.
// Full license details are defined in the 'LICENSE' file.
//
using System;
using NUnit.Framework;
using Emul8.Time;
using System.Collections.Generic;

namespace UnitTests
{
    [TestFixture]
    public class TimeTests
    {
        [Test]
        public void ShouldTickWithOneHandler()
        {
            var clocksource = new BaseClockSource();
            var counter = 0;

            clocksource.AddClockEntry(new ClockEntry(2, 1, () => counter++) { Value = 0 });
            clocksource.Advance(1);
            Assert.AreEqual(0, counter);
            clocksource.Advance(1);
            Assert.AreEqual(1, counter);
            clocksource.Advance(1);
            Assert.AreEqual(1, counter);
            clocksource.Advance(2);
            Assert.AreEqual(2, counter);
        }

        [Test]
        public void ShouldTickWithTwoHandlers()
        {
            var clocksource = new BaseClockSource();
            var counterA = 0;
            var counterB = 0;

            clocksource.AddClockEntry(new ClockEntry(2, 1, () => counterA++) { Value = 0 });
            clocksource.AddClockEntry(new ClockEntry(5, 1, () => counterB++) { Value = 0 });
            clocksource.Advance(2);
            Assert.AreEqual(1, counterA);
            Assert.AreEqual(0, counterB);
            clocksource.Advance(2);
            Assert.AreEqual(2, counterA);
            Assert.AreEqual(0, counterB);
            clocksource.Advance(1);
            Assert.AreEqual(2, counterA);
            Assert.AreEqual(1, counterB);
        }

        [Test]
        public void ShouldHaveHandlersInSync()
        {
            // we test here whether handler executed by the slower clock entry
            // always "sees" value of the faster one as ten times its own value
            var clockSource = new BaseClockSource();

            Action firstHandler = () =>
            {
            };

            var values = new List<long>();

            // clock entry with ratio -10 is 10 times slower than the one with 1
            clockSource.AddClockEntry(new ClockEntry(10000, 1, firstHandler));
            clockSource.AddClockEntry(new ClockEntry(1, -10, () => values.Add(clockSource.GetClockEntry(firstHandler).Value)));

            clockSource.Advance(9, true);
            clockSource.Advance(8, true);
            clockSource.Advance(20, true);

            CollectionAssert.AreEqual(new [] { 10, 20, 30 }, values);
        }
    }
}

