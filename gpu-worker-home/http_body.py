import re


MAX_REQUEST_BYTES = 65536


class BodyTooLarge(ValueError):
    pass


def read_http_body(headers, stream):
    encodings = headers.get_all("Transfer-Encoding", [])
    lengths = headers.get_all("Content-Length", [])
    if encodings:
        if lengths or len(encodings) != 1 or encodings[0].strip().lower() != "chunked":
            raise ValueError("Unsupported request framing.")
        return read_chunked_body(stream)

    if not lengths:
        return b""
    if len(lengths) != 1 or not re.fullmatch(r"[0-9]{1,15}", lengths[0]):
        raise ValueError("Invalid content length.")
    size = int(lengths[0])
    if size > MAX_REQUEST_BYTES:
        raise BodyTooLarge()
    body = stream.read(size)
    if len(body) != size:
        raise ValueError("Incomplete request body.")
    return body


def read_chunked_body(stream):
    body = bytearray()
    while True:
        line = stream.readline(129)
        size_hex = line[:-2].split(b";", 1)[0]
        if not line.endswith(b"\r\n") or not re.fullmatch(rb"[0-9a-fA-F]{1,16}", size_hex):
            raise ValueError("Invalid request chunk.")
        size = int(size_hex, 16)
        if not size:
            break
        if len(body) + size > MAX_REQUEST_BYTES:
            raise BodyTooLarge()
        chunk = stream.read(size)
        if len(chunk) != size or stream.read(2) != b"\r\n":
            raise ValueError("Incomplete request chunk.")
        body.extend(chunk)

    trailer_budget = 4096
    while trailer_budget > 0:
        trailer = stream.readline(trailer_budget + 1)
        if trailer == b"\r\n":
            return bytes(body)
        if not trailer.endswith(b"\r\n"):
            break
        trailer_budget -= len(trailer)
    raise ValueError("Invalid request trailers.")
