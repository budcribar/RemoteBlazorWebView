# RemoteBlazorWebViewServer.exe

## Size:
187,383,398

### dotnet8
| Run  | Result 1 | Result 2 | Result 3 | Result 4 |
|------|----------|----------|----------|----------|
| 1st  | 2247     | 2248     | 2262     | 2251     |
| 2nd  | 2        | 2        | 1        | 2        |
| 3rd  | 2        | 2        | 1        | 2        |
| 4th  | 1        | 0        | 0        | 0        |

## Used AOT

### Size:
31,302,656

### dotnet8
| Run  | Result 1 | Result 2 | Result 3 |
|------|----------|----------|----------|
| 1st  | 2569     | 2205     | 2258     |
| 2nd  | 6        | 5        | 1        |
| 3rd  | 6        | 5        | 1        |
| 4th  | 1        | 1        | 0        |


### dotnet9 Release Mode Size 32,243,712

| Run  | Result 1 | Result 2 | Result 3 |
|------|----------|----------|----------|
| 1st  | 2241ms   |  2260ms  | 2272     |
| 2nd  | 1ms      |     1ms  | 1        |
| 3rd  | 1ms      |     1ms  | 1        |
| 4th  | 0ms      |     1ms  | 0        |
| 500  | 96 ms    |    91ms  | 80ms     |
|      | 24ms     |    24ms  | 24ms     | 100 GetServerStatusAsync
|      | 20ms     |    20ms  | 20ms     | 125 GetServerStatusAsync
|      | 34ms     |    34ms  | 34ms     | 125 CreateWebViewRequest