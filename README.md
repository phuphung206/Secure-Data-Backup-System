# 🛡️ Secure Data Backup System

Hệ thống sao lưu và phục hồi dữ liệu tự động theo mô hình Client-Server. Đồ án được thiết kế để giải quyết bài toán đồng bộ dữ liệu an toàn qua mạng, quản lý luồng hệ thống (multithreading), và tối ưu hóa dung lượng lưu trữ thông qua cơ chế Incremental Backup.

## Tính năng nổi bật
*   **Quản lý cấu hình:** Giao diện cho phép người dùng chọn thư mục/file cần backup (hỗ trợ thao tác kéo thả, lọc theo định dạng file, ví dụ `*.docx;*.pdf`).
*   **Cơ chế Backup:**
    *   **Full Backup:** Sao lưu toàn bộ dữ liệu định kỳ.
    *   **Incremental Backup:** Chỉ sao lưu những file có sự thay đổi (dựa vào kích thước, thời gian chỉnh sửa và mã băm SHA-256) kể từ lần backup gần nhất để tiết kiệm băng thông.
*   **Lên lịch tự động (Scheduler):** Thiết lập tác vụ chạy ngầm bằng Windows Task Scheduler để tự động backup hằng ngày, mỗi 2/3 ngày hoặc hằng tuần, lưu cục bộ hoặc gửi lên Server (Full hoặc Incremental).
*   **Quản lý phiên bản (Version Control) & Phục hồi (Restore):** Mỗi lần backup lên Server tạo một phiên bản. Người dùng xem lại lịch sử và chọn khôi phục toàn bộ thư mục hoặc một file đơn lẻ về đúng thời điểm mong muốn.
*   **Bảo mật & Mã hóa:** Mã hóa dữ liệu AES-256 ở phía Client trước khi truyền qua mạng. Dữ liệu dù bị đánh cắp trên đường truyền hay trên Server cũng không thể đọc được nếu không có mật khẩu của Client.
*   **Xử lý Đa luồng (Multithreading):** Nén file và gửi qua mạng dưới nền (BackgroundWorker), chia nhỏ file lớn (chunking) để gửi và hiển thị thanh tiến trình (progress bar) mà không làm treo giao diện.
*   **Chống Ransomware bằng AI:** Module Python phân tích độ bất thường (Shannon entropy, đuôi file lạ, tỷ lệ file bị mã hóa). Nếu phát hiện file bị đổi đuôi hoặc mã hóa hàng loạt, chương trình chặn ngay việc backup để không ghi đè dữ liệu rác lên bản backup sạch trên Server. Khi máy không có Python, engine C# tương đương được dùng thay.

### Phía Client (Ứng dụng người dùng)
*   **Incremental & Full Backup:** Client so sánh với snapshot của phiên bản trước **do Server trả về** (kích thước + `LastWriteTimeUtc`, sau đó SHA-256), nên kết quả luôn khớp dữ liệu thực có trên Server.
*   **Pipeline gửi file:** Lọc định dạng → AI chống Ransomware → Nén GZip → Mã hóa AES-256 → chia chunk → gửi TCP → Server kiểm tra SHA-256.
*   **Real-time Monitoring:** `FileSystemWatcher` giám sát thêm/sửa/xóa/đổi tên. Có thể bật **đồng bộ lên Server**: các thay đổi liên tiếp được gom lại (3 giây) rồi chạy một lần Incremental, tạo phiên bản mới (kích hoạt = Real-time).
*   **Lên lịch tự động (Scheduler):** Windows Task Scheduler gọi `SecureDataBackupSystem.exe --scheduled-backup`.
*   **Quản lý phiên bản & Restore:** Màn hình “Phiên bản” liệt kê các lần backup (thời gian, Full/Incremental, Thủ công/Lịch/Real-time, số file, dung lượng) và nội dung từng phiên bản; khôi phục toàn bộ thư mục hoặc các file được chọn.

### Phía Server (Lưu trữ tập trung)
*   **Truyền tải mạng tin cậy:** TCP Socket đa luồng (mỗi Client một Task), gói tin tự định nghĩa có CRC-32, chunking cho file lớn, xác thực SHA-256 khi ráp file.
*   **Quản lý Metadata (SQLite):** Lưu người dùng (mật khẩu băm PBKDF2), lịch sử phiên bản và ánh xạ đường dẫn vật lý của từng file trong từng phiên bản.
*   **Snapshot không nhân bản dữ liệu:** File không đổi được “kế thừa” từ phiên bản trước (chỉ thêm dòng metadata trỏ tới file vật lý cũ); file bị xóa ở Client không xuất hiện trong snapshot mới.
*   **An toàn đường dẫn:** Mọi `BackupId`/đường dẫn tương đối từ Client được kiểm tra chống Path Traversal (`..`, đường dẫn tuyệt đối, ổ đĩa) trước khi ghi/đọc.

## Kiến trúc & Công nghệ sử dụng

*   **Ngôn ngữ & Nền tảng:** C# — Client WinForms trên .NET Framework 4.7.2; Server, thư viện Client và bộ kiểm thử trên .NET 10.
*   **Giao diện (GUI):** Windows Forms.
*   **Giao tiếp mạng:** TCP Socket với cấu trúc Packet tự định nghĩa (Magic `SDBS`, loại gói, độ dài, CRC-32).
*   **Cơ sở dữ liệu Server:** SQLite (`Microsoft.Data.Sqlite`), file tại `<thư mục lưu trữ>/Database/BackupServer.sqlite`, tự tạo và tự nâng cấp schema.
*   **Xử lý luồng:** `Task`, `async/await` và `BackgroundWorker`.
*   **AI:** Python 3 (`BackupSystem.AI/ransomware_detector.py`), không cần thư viện ngoài.

| Project | Vai trò |
|---|---|
| `SecureDataBackupSystem` | Ứng dụng WinForms (Client) |
| `BackupSystem.Client` | Engine sao lưu mạng, mã hóa, nén, khôi phục theo phiên bản, AI chống Ransomware |
| `BackupSystem.Server` | TCP Server, lưu trữ file, SQLite Metadata |
| `BackupSystem.Server.App` | Chương trình console chạy Server |
| `BackupSystem.Shared` | Giao thức mạng, model, chunking, kiểm tra đường dẫn an toàn |
| `BackupSystem.Tests` | 14 kịch bản kiểm thử tự động |

### Mã hóa
Định dạng file mã hóa (`SDBAESv2`): AES-256-CBC + HMAC-SHA256 (Encrypt-then-MAC), khóa 64 byte phái sinh từ mật khẩu bằng PBKDF2-SHA256 200.000 vòng, salt/IV ngẫu nhiên cho từng file. HMAC được kiểm tra trước khi giải mã nên sai mật khẩu hoặc dữ liệu bị sửa trên Server đều bị phát hiện. Bản backup cũ (`SDBAES256`) vẫn giải mã được.

### Luồng một lần sao lưu lên Server
1. Client đăng nhập (`AuthRequest`).
2. Mở phiên bản (`BackupSessionStartRequest`) → Server trả `VersionId` và snapshot của phiên bản trước.
3. Gửi các file mới/thay đổi (`BackupInitRequest` → `FileChunkData` → `BackupCompleteRequest`); Server ghi metadata của từng file.
4. Đóng phiên bản (`BackupSessionEndRequest`) kèm danh sách file hiện có → Server kế thừa file không đổi và chốt phiên bản `COMPLETED`. Phiên bị hủy hoặc rớt mạng được đánh dấu `FAILED` và không được dùng làm bản gốc cho Incremental.

## Hướng dẫn cài đặt (Installation)

### Yêu cầu hệ thống
*   Visual Studio 2022 hoặc mới hơn.
*   .NET Framework 4.7.2 (Client WinForms) và .NET 10 SDK (Server, thư viện, kiểm thử).
*   Python 3 (không bắt buộc, dùng cho module AI).
*   Không cần cài đặt máy chủ CSDL: SQLite được nhúng sẵn trong Server.

### Chạy hệ thống
1.  Khởi động Server:
    ```
    dotnet run --project SecureDataBackupSystem/BackupSystem.Server.App [cổng] [thư mục lưu trữ]
    ```
    Mặc định cổng `5050`. Tài khoản khởi tạo sẵn: **admin / 123456**.
2.  Tạo thêm tài khoản hoặc đổi mật khẩu:
    ```
    dotnet run --project SecureDataBackupSystem/BackupSystem.Server.App adduser <tên> <mật khẩu> [thư mục lưu trữ]
    dotnet run --project SecureDataBackupSystem/BackupSystem.Server.App passwd  <tên> <mật khẩu mới> [thư mục lưu trữ]
    ```
3.  Mở `SecureDataBackupSystem.slnx` bằng Visual Studio, chạy project `SecureDataBackupSystem` (WinForms).
4.  Vào **Sao lưu mạng**, nhập IP/Port Server và tài khoản, chọn dữ liệu rồi bấm **Bắt đầu sao lưu**. Cấu hình kết nối được lưu lại (mật khẩu bảo vệ bằng DPAPI) để Lập lịch và Real-time dùng chung.
5.  Khôi phục theo thời điểm: nút **🕘 Phiên bản** (màn hình Sao lưu mạng) hoặc **Theo phiên bản (Server)...** (màn hình Khôi phục).

### Chạy kiểm thử
```
dotnet run --project SecureDataBackupSystem/BackupSystem.Tests
```
