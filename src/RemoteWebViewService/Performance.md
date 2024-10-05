Server Performance Benchmarks

Version 9.0.rc1 ClientFileReadResponse channel bounded to 1 no file read lock

MultipleConcurrentFileRequestsWithSameFile_ReturnsCorrectResponses 500x3 = 1500 files 2.7 sec
MultipleConcurrentFileRequests_ReturnsCorrectResponses 100 files  2.2 seconds


Version 9.0.rc1 ClientFileReadResponse channel unbounded no file read lock
MultipleConcurrentFileRequestsWithSameFile_ReturnsCorrectResponses 500x3 = 1500 files 2.6 sec
MultipleConcurrentFileRequests_ReturnsCorrectResponses 100 files  2.2 seconds
MultipleConcurrentFileRequestsWithSameFile_ReturnsCorrectResponses 1000x3 = 3000 files 3 sec
MultipleConcurrentFileRequestsWithSameFile_ReturnsCorrectResponses 2000x3 = 6000 files hangs
MultipleConcurrentFileRequestsWithSameFile_ReturnsCorrectResponses 1500x3 = 4500 files 3.2 sec
MultipleConcurrentFileRequestsWithSameFile_ReturnsCorrectResponses 1750x3 = 5250 files hangs
MultipleConcurrentFileRequestsWithSameFile_ReturnsCorrectResponses 1625x3 = 4875 files 3.3
MultipleConcurrentFileRequestsWithSameFile_ReturnsCorrectResponses 1667x3 = 5001 files 5.3 sec

MultipleConcurrentFileRequestsWithSameFile_BrowserAndNoClientCaching 100x3 = 300 pages 14.6 sec
MultipleConcurrentFileRequestsWithSameFile_BrowserAndClientCaching 100x3 = 300 pages 14.8 sec
