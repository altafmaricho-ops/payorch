# PayOrchestrator Merchant Integration

PayOrchestrator is designed as an optional payment gateway for merchant websites. A merchant can keep its existing gateways and add PayOrchestrator as another provider.

## 1. Merchant onboarding

An operator creates a Website/Tenant in the control plane. The platform returns:
- `apiKey` — used in `X-Api-Key` on merchant API calls.
- `apiSecret` — shown once and stored by the merchant securely.
- `callbackSecret` — shown once; PayOrchestrator uses it to sign outbound payment webhooks.

The website must be Active before production payment creation is accepted.

## 2. Create a payment

`POST /api/v1/payments`

Headers:

```http
X-Api-Key: pk_live_xxxxxxxxx
Content-Type: application/json
```

Body:

```json
{
  "merchantOrderId": "ORDER-10001",
  "amount": 499.00,
  "currency": "INR",
  "paymentMethod": "upi",
  "customer": {
    "name": "Customer Name",
    "email": "customer@example.com",
    "phone": "9876543210"
  },
  "customerRef": "CUSTOMER-42",
  "returnUrl": "https://merchant.example/payment/return"
}
```

The response contains `paymentId`, status and a `checkout` object. The merchant does not select Razorpay or another PSP in this request. Smart Routing decides that internally.

## 3. Query payment status

```http
GET /api/v1/payments/{paymentId}
X-Api-Key: pk_live_xxxxxxxxx
```

Or query by the merchant's own reference:

```http
GET /api/v1/payments/by-reference/ORDER-10001
X-Api-Key: pk_live_xxxxxxxxx
```

## 4. Merchant webhook

Configure the website callback URL in the control plane. PayOrchestrator sends verified terminal payment events to that URL.

Header:

```http
X-PayOrch-Signature: <HMAC-SHA256-hex>
```

The signature is calculated over the exact JSON request body using the merchant's callback secret.

Example event:

```json
{
  "orderId": "...",
  "merchantOrderRef": "ORDER-10001",
  "amount": 499.0,
  "currency": "INR",
  "status": "success",
  "completedAt": "2026-10-05T08:00:00Z"
}
```

Return any HTTP 2xx response after validating the signature and idempotently recording the event.

## 5. Architecture guarantee

Merchant code remains stable when the operator changes routing from Razorpay to another PSP. PSP credentials, routing rules, fallback providers, webhook verification and retry processing remain inside PayOrchestrator.
