#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
Module AI / Heuristic Anomaly Detection: Phát hiện bất thường & Chống Ransomware
Tự động phân tích Entropy Shannon, đuôi file bất thường và tỷ lệ mã hóa hàng loạt.
Khi phát hiện dấu hiệu virus tống tiền (Ransomware), trả về cảnh báo để chặn ngay lập tức quá trình Backup.
"""

import sys
import os
import math
import json
import argparse
from typing import List, Dict, Any

# Danh sách phần mở rộng thường thấy ở các chủng Ransomware phổ biến
SUSPICIOUS_EXTENSIONS = {
    # Không có ".enc": đây là đuôi file mã hóa hợp lệ do chính Secure Data Backup tạo ra.
    ".locked", ".crypto", ".wnry", ".crypted", ".ransom",
    ".locky", ".cerber", ".wannacry", ".crypt", ".payme", ".darkside",
    ".revil", ".conti", ".blackmatter", ".stop", ".djvu", ".phobos",
    ".dharma", ".ryuk", ".makop", ".mallox", ".zepto", ".thor"
}

# Các đuôi file thông thường
BENIGN_EXTENSIONS = {
    ".txt", ".docx", ".doc", ".xlsx", ".xls", ".pptx", ".ppt",
    ".pdf", ".cs", ".cpp", ".c", ".h", ".java", ".py", ".html",
    ".css", ".js", ".json", ".xml", ".csv", ".sql", ".md"
}

def calculate_shannon_entropy(file_path: str, max_sample_bytes: int = 65536) -> float:
    """
    Tính toán độ hỗn loạn thông tin (Shannon Entropy) trên mẫu byte của file.
    Giá trị nằm trong khoảng [0.0, 8.0]:
    - Text/Code/Văn bản thông thường: 2.5 - 5.5
    - File nén/Media thông thường: 6.5 - 7.5
    - File bị mã hóa (Ransomware / AES): 7.6 - 8.0 (tiệm cận ngẫu nhiên tuyệt đối)
    """
    if not os.path.exists(file_path) or os.path.isdir(file_path):
        return 0.0

    try:
        size = os.path.getsize(file_path)
        if size == 0:
            return 0.0

        with open(file_path, "rb") as f:
            data = f.read(max_sample_bytes)

        if not data:
            return 0.0

        length = len(data)
        byte_counts = [0] * 256
        for b in data:
            byte_counts[b] += 1

        entropy = 0.0
        for count in byte_counts:
            if count > 0:
                p = count / length
                entropy -= p * math.log2(p)

        return round(entropy, 4)
    except Exception:
        return 0.0

def is_own_encrypted_backup(file_path: str) -> bool:
    """
    File mã hóa do chính Secure Data Backup tạo ra bắt đầu bằng header "SDB" + A/F/P/E
    (SDBAES256, SDBAESv2, SDBF, SDBP, SDBE). Entropy cao của các file này là bình thường.
    """
    try:
        with open(file_path, "rb") as f:
            header = f.read(4)
        return len(header) == 4 and header[:3] == b"SDB" and header[3:4] in (b"A", b"F", b"P", b"E")
    except Exception:
        return False


def analyze_target(target_path: str, specific_files: List[str] = None) -> Dict[str, Any]:
    """
    Quét và phân tích độ bất thường của tập tin để phát hiện tấn công Ransomware.
    """
    files_to_check = []

    if specific_files:
        files_to_check = [f for f in specific_files if os.path.isfile(f)]
    elif os.path.isfile(target_path):
        files_to_check = [target_path]
    elif os.path.isdir(target_path):
        for root, _, files in os.walk(target_path):
            for file in files:
                files_to_check.append(os.path.join(root, file))
                if len(files_to_check) >= 500: # Giới hạn lấy mẫu 500 file lớn nhất/mới nhất để phân tích nhanh
                    break
            if len(files_to_check) >= 500:
                break

    own_encrypted = {f for f in files_to_check if is_own_encrypted_backup(f)}
    files_to_check = [f for f in files_to_check if f not in own_encrypted]

    total_files = len(files_to_check)
    if total_files == 0:
        return {
            "is_ransomware_detected": False,
            "anomaly_score": 0.0,
            "scanned_files_count": 0,
            "suspicious_files_count": 0,
            "high_entropy_files_count": 0,
            "message": "Không tìm thấy file nào để phân tích."
        }

    suspicious_extension_files = []
    high_entropy_files = []
    total_entropy = 0.0

    for fpath in files_to_check:
        ext = os.path.splitext(fpath)[1].lower()
        entropy = calculate_shannon_entropy(fpath)
        total_entropy += entropy

        # 1. Kiểm tra đuôi file nghi vấn Ransomware
        if ext in SUSPICIOUS_EXTENSIONS:
            suspicious_extension_files.append(fpath)
        # Kiểm tra đuôi kép (Double Extension ví dụ: tailieu.docx.locked)
        parts = os.path.basename(fpath).split(".")
        if len(parts) >= 3 and parts[-1].lower() in SUSPICIOUS_EXTENSIONS:
            if fpath not in suspicious_extension_files:
                suspicious_extension_files.append(fpath)

        # 2. Kiểm tra Entropy cao (> 7.65)
        # Nếu file là văn bản thông thường (docx, txt, pdf) mà entropy > 7.7 thì rất đáng ngờ
        if entropy >= 7.65:
            high_entropy_files.append((fpath, entropy))

    avg_entropy = total_entropy / total_files if total_files > 0 else 0.0
    suspicious_ext_ratio = len(suspicious_extension_files) / total_files
    high_entropy_ratio = len(high_entropy_files) / total_files

    # Thuật toán tính chỉ số bất thường Anomaly Score (0.0 đến 1.0)
    # Trọng số: 55% Đuôi file Ransomware + 35% Entropy cao (hàng loạt) + 10% Entropy trung bình toàn cục
    effective_entropy_ratio = high_entropy_ratio if len(high_entropy_files) >= 3 else (high_entropy_ratio * 0.5)
    anomaly_score = (effective_entropy_ratio * 0.35) + (suspicious_ext_ratio * 0.55)
    if avg_entropy > 7.3 and (len(high_entropy_files) >= 3 or len(suspicious_extension_files) > 0):
        anomaly_score += 0.10

    anomaly_score = min(1.0, round(anomaly_score, 4))

    # Điều kiện phát hiện Ransomware:
    # 1. Có ít nhất 3 file hoặc >= 20% số file bị đổi sang đuôi Ransomware đã biết
    # 2. Hoặc >= 5 file và >= 35% số file có entropy xấp xỉ 8.0 (dấu hiệu bị mã hóa hàng loạt)
    # 3. Hoặc Anomaly Score >= 0.50
    is_detected = False
    warning_reasons = []

    if len(suspicious_extension_files) >= 3 or (total_files > 0 and suspicious_ext_ratio >= 0.20):
        is_detected = True
        warning_reasons.append(f"Phát hiện {len(suspicious_extension_files)} file mang phần mở rộng của virus tống tiền ({', '.join(set([os.path.splitext(f)[1] for f in suspicious_extension_files]))}).")

    if len(high_entropy_files) >= 5 and high_entropy_ratio >= 0.35:
        is_detected = True
        warning_reasons.append(f"Phát hiện {len(high_entropy_files)}/{total_files} file có Entropy cực cao (> 7.65) - Dấu hiệu bị mã hóa hàng loạt!")

    if anomaly_score >= 0.50:
        is_detected = True

    if is_detected:
        msg = "CẢNH BÁO RANSOMWARE: " + " ".join(warning_reasons)
    else:
        msg = f"An toàn. Đã quét {total_files} file (Entropy trung bình: {avg_entropy:.2f}, Anomaly Score: {anomaly_score})."

    return {
        "IsRansomwareDetected": is_detected,
        "AnomalyScore": anomaly_score,
        "ScannedFilesCount": total_files,
        "SuspiciousFilesCount": len(suspicious_extension_files),
        "HighEntropyFilesCount": len(high_entropy_files),
        "AverageEntropy": round(avg_entropy, 2),
        "Message": msg,
        "is_ransomware_detected": is_detected,
        "anomaly_score": anomaly_score,
        "scanned_files_count": total_files,
        "suspicious_files_count": len(suspicious_extension_files),
        "high_entropy_files_count": len(high_entropy_files),
        "average_entropy": round(avg_entropy, 2),
        "message": msg
    }

def main():
    parser = argparse.ArgumentParser(description="Secure Data Backup AI - Ransomware Anomaly Detector")
    parser.add_argument("target", nargs="?", default="", help="Đường dẫn thư mục hoặc file cần quét")
    parser.add_argument("--files", nargs="*", default=None, help="Danh sách các file cụ thể cần phân tích")
    parser.add_argument("--json", action="store_true", default=True, help="Xuất kết quả định dạng JSON")

    args = parser.parse_args()

    if not args.target and not args.files:
        result = {
            "is_ransomware_detected": False,
            "anomaly_score": 0.0,
            "message": "Vui lòng truyền đường dẫn thư mục hoặc file cần kiểm tra."
        }
        print(json.dumps(result, ensure_ascii=False))
        sys.exit(0)

    target_path = args.target if args.target else ""
    result = analyze_target(target_path, args.files)

    print(json.dumps(result, ensure_ascii=False, indent=2))
    # Trả về exit code 2 nếu phát hiện Ransomware để caller C# dễ dàng bắt
    if result["is_ransomware_detected"]:
        sys.exit(2)
    else:
        sys.exit(0)

if __name__ == "__main__":
    main()
