﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Net.Http.Headers;
using Xunit;

namespace Microsoft.AspNetCore.Internal
{
    public class RangeHelperTests
    {
        [Theory]
        [InlineData(1, 2)]
        [InlineData(2, 3)]
        public void NormalizeRange_ReturnsNullWhenRangeStartEqualsOrGreaterThanLength(long start, long end)
        {
            // Arrange & Act
            var normalizedRange = RangeHelper.NormalizeRange(new RangeItemHeaderValue(start, end), 1);

            // Assert
            Assert.Null(normalizedRange);
        }

        [Fact]
        public void NormalizeRange_ReturnsNullWhenRangeEndEqualsZero()
        {
            // Arrange & Act
            var normalizedRange = RangeHelper.NormalizeRange(new RangeItemHeaderValue(null, 0), 1);

            // Assert
            Assert.Null(normalizedRange);
        }

        [Theory]
        [InlineData(0, null, 0, 2)]
        [InlineData(0, 0, 0, 0)]
        public void NormalizeRange_ReturnsNormalizedRange(long? start, long? end, long? normalizedStart, long? normalizedEnd)
        {
            // Arrange & Act
            var normalizedRange = RangeHelper.NormalizeRange(new RangeItemHeaderValue(start, end), 3);

            // Assert
            Assert.Equal(normalizedStart, normalizedRange.From);
            Assert.Equal(normalizedEnd, normalizedRange.To);
        }

        [Fact]
        public void NormalizeRange_ReturnsRangeWithNoChange()
        {
            // Arrange & Act
            var normalizedRange = RangeHelper.NormalizeRange(new RangeItemHeaderValue(1, 3), 4);

            // Assert
            Assert.Equal(1, normalizedRange.From);
            Assert.Equal(3, normalizedRange.To);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        public void ParseRange_ReturnsNullWhenRangeHeaderNotProvided(string range)
        {
            // Arrange
            var httpContext = new DefaultHttpContext();
            httpContext.Request.Headers[HeaderNames.Range] = range;

            // Act
            var (isRangeRequest, parsedRangeResult) = RangeHelper.ParseRange(httpContext, httpContext.Request.GetTypedHeaders(), 10, NullLogger.Instance);

            // Assert
            Assert.False(isRangeRequest);
            Assert.Null(parsedRangeResult);
        }

        [Theory]
        [InlineData("1-2, 3-4")]
        [InlineData("1-2, ")]
        public void ParseRange_ReturnsNullWhenMultipleRangesProvidedInRangeHeader(string range)
        {
            // Arrange
            var httpContext = new DefaultHttpContext();
            httpContext.Request.Headers[HeaderNames.Range] = range;

            // Act
            var (isRangeRequest, parsedRangeResult) = RangeHelper.ParseRange(httpContext, httpContext.Request.GetTypedHeaders(), 10, NullLogger.Instance);

            // Assert
            Assert.False(isRangeRequest);
            Assert.Null(parsedRangeResult);
        }

        [Fact]
        public void ParseRange_ReturnsSingleRangeWhenInputValid()
        {
            // Arrange
            var httpContext = new DefaultHttpContext();
            var range = new RangeHeaderValue(1, 2);
            httpContext.Request.Headers[HeaderNames.Range] = range.ToString();

            // Act
            var (isRangeRequest, parsedRange) = RangeHelper.ParseRange(httpContext, httpContext.Request.GetTypedHeaders(), 4, NullLogger.Instance);

            // Assert
            Assert.True(isRangeRequest);
            Assert.Equal(1, parsedRange.From);
            Assert.Equal(2, parsedRange.To);
        }
    }
}
