import { useEffect, useRef, useState } from 'react';
import { ArrowUpRight, ChevronDown, Clock3, History, Leaf, Moon, Send, Sparkles, Sun } from 'lucide-react';
import { Alert, AlertDescription } from '@/components/ui/alert';
import { Badge } from '@/components/ui/badge';
import { Bubble, BubbleContent } from '@/components/ui/bubble';
import { Button } from '@/components/ui/button';
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card';
import { Empty, EmptyDescription, EmptyHeader, EmptyMedia, EmptyTitle } from '@/components/ui/empty';
import { Field, FieldDescription, FieldGroup, FieldLabel } from '@/components/ui/field';
import { Input } from '@/components/ui/input';
import { InputGroup, InputGroupAddon, InputGroupTextarea } from '@/components/ui/input-group';
import { Message, MessageAvatar, MessageContent } from '@/components/ui/message';
import { MessageScroller, MessageScrollerButton, MessageScrollerContent, MessageScrollerItem, MessageScrollerProvider, MessageScrollerViewport } from '@/components/ui/message-scroller';
import { Separator } from '@/components/ui/separator';
import { Sheet, SheetContent, SheetDescription, SheetHeader, SheetTitle, SheetTrigger } from '@/components/ui/sheet';
import { Spinner } from '@/components/ui/spinner';

const starters = [
  { icon: Sparkles, title: 'Explore the menu', prompt: 'Show me the menu and recommend a few favorites.' },
  { icon: Leaf, title: 'Find my kind of food', prompt: 'I’m vegetarian. What would you recommend?' },
  { icon: Send, title: 'Start an order', prompt: 'Help me choose a meal for two and then check out with demo-card.' },
  { icon: Clock3, title: 'Track an order', prompt: 'What is the status of my order?' },
];

function initialMessage() {
  return [{ id: 'welcome', role: 'assistant', text: 'Hi there! I’m your table-side guide. I can help you explore the menu, plan an order, or check on a local demo order. What sounds good today?' }];
}

export default function App() {
  const [customerId, setCustomerId] = useState('guest-1');
  const [messages, setMessages] = useState(initialMessage);
  const [draft, setDraft] = useState('');
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState('');
  const [order, setOrder] = useState(null);
  const [orderError, setOrderError] = useState('');
  const [history, setHistory] = useState([]);
  const [historyLoading, setHistoryLoading] = useState(false);
  const [historyError, setHistoryError] = useState('');
  const [historyOpen, setHistoryOpen] = useState(false);
  const [darkMode, setDarkMode] = useState(() => document.documentElement.classList.contains('dark'));
  const inputRef = useRef(null);
  const restoreInputFocusRef = useRef(false);
  const customerIdRef = useRef(customerId);
  customerIdRef.current = customerId;

  useEffect(() => {
    if (!loading && restoreInputFocusRef.current) {
      restoreInputFocusRef.current = false;
      inputRef.current?.focus();
    }
  }, [loading]);

  useEffect(() => {
    const id = customerId.trim();
    if (!id) {
      setOrder(null);
      setOrderError('');
      setHistory([]);
      setHistoryLoading(false);
      setHistoryError('');
      setHistoryOpen(false);
      return undefined;
    }

    const controller = new AbortController();
    let active = true;
    async function refreshCurrentOrder() {
      try {
        const response = await fetch(`/api/orders/current?customerId=${encodeURIComponent(id)}`, { signal: controller.signal });
        const data = await response.json();
        if (!response.ok) throw new Error(data?.error || 'The order summary could not be loaded.');
        if (active) {
          setOrder(data?.order ?? null);
          setOrderError('');
        }
      } catch (cause) {
        if (active && cause.name !== 'AbortError') {
          setOrderError(cause instanceof Error ? cause.message : 'The order summary could not be loaded.');
        }
      }
    }

    async function refreshPastOrders() {
      try {
        const response = await fetch(`/api/orders/history?customerId=${encodeURIComponent(id)}`, { signal: controller.signal });
        const data = await response.json();
        if (!response.ok) throw new Error(data?.error || 'Order history could not be loaded.');
        if (active) {
          setHistory(Array.isArray(data?.orders) ? data.orders : []);
          setHistoryError('');
        }
      } catch (cause) {
        if (active && cause.name !== 'AbortError') {
          setHistoryError(cause instanceof Error ? cause.message : 'Order history could not be loaded.');
        }
      }
    }

    refreshCurrentOrder();
    refreshPastOrders();
    const interval = order ? window.setInterval(refreshCurrentOrder, 5000) : undefined;
    return () => {
      active = false;
      controller.abort();
      if (interval) window.clearInterval(interval);
    };
  }, [customerId, order?.id]);

  function changeCustomerId(value) {
    setCustomerId(value);
    setMessages(initialMessage());
    setDraft('');
    setOrder(null);
    setOrderError('');
    setHistory([]);
    setHistoryLoading(false);
    setHistoryError('');
    setHistoryOpen(false);
    setError('');
  }

  async function refreshOrder(id = customerId.trim()) {
    if (!id) return;
    try {
      const response = await fetch(`/api/orders/current?customerId=${encodeURIComponent(id)}`);
      const data = await response.json();
      if (!response.ok) throw new Error(data?.error || 'The order summary could not be loaded.');
      if (id === customerIdRef.current.trim()) {
        setOrder(data?.order ?? null);
        setOrderError('');
      }
    } catch (cause) {
      if (id === customerIdRef.current.trim()) {
        setOrderError(cause instanceof Error ? cause.message : 'The order summary could not be loaded.');
      }
    }
  }

  async function refreshHistory(id = customerId.trim()) {
    if (!id) return;
    setHistoryLoading(true);
    try {
      const response = await fetch(`/api/orders/history?customerId=${encodeURIComponent(id)}`);
      const data = await response.json();
      if (!response.ok) throw new Error(data?.error || 'Order history could not be loaded.');
      if (id === customerIdRef.current.trim()) {
        setHistory(Array.isArray(data?.orders) ? data.orders : []);
        setHistoryError('');
      }
    } catch (cause) {
      if (id === customerIdRef.current.trim()) {
        setHistoryError(cause instanceof Error ? cause.message : 'Order history could not be loaded.');
      }
    } finally {
      if (id === customerIdRef.current.trim()) setHistoryLoading(false);
    }
  }

  async function sendMessage(message = draft) {
    const text = message.trim();
    const id = customerId.trim();
    if (!text || loading) return;
    if (!id) {
      setError('Add a customer name or ID before sending a message.');
      return;
    }

    setError('');
    setDraft('');
    setMessages((current) => [...current, { id: crypto.randomUUID(), role: 'user', text }]);
    setLoading(true);
    try {
      const response = await fetch('/api/chat', {
        method: 'POST',
        headers: { 'content-type': 'application/json' },
        body: JSON.stringify({ customerId: id, message: text }),
      });
      const data = await response.json();
      if (!response.ok) throw new Error(data?.error || 'The chat could not complete your request. Please try again.');
      if (typeof data?.response !== 'string') throw new Error('The chat returned an unexpected response. Please try again.');
      if (id === customerIdRef.current.trim()) {
        setMessages((current) => [...current, { id: crypto.randomUUID(), role: 'assistant', text: data.response }]);
        await Promise.all([refreshOrder(id), refreshHistory(id)]);
      }
    } catch (cause) {
      if (id === customerIdRef.current.trim()) {
        setError(cause instanceof Error ? cause.message : 'Something went wrong. Please try again.');
      }
    } finally {
      restoreInputFocusRef.current = true;
      setLoading(false);
    }
  }

  function toggleTheme() {
    const nextDarkMode = !darkMode;
    setDarkMode(nextDarkMode);
    document.documentElement.classList.toggle('dark', nextDarkMode);
    document.documentElement.style.colorScheme = nextDarkMode ? 'dark' : 'light';
    document.querySelector('meta[name="theme-color"]')?.setAttribute('content', nextDarkMode ? '#171717' : '#ffffff');
    try {
      window.localStorage.setItem('olive-ember-theme', nextDarkMode ? 'dark' : 'light');
    } catch {
      // The theme still changes for this session when storage is unavailable.
    }
  }

  function handleKeyDown(event) {
    if (event.key === 'Enter' && !event.shiftKey) {
      event.preventDefault();
      sendMessage();
    }
  }

  return (
    <main className="flex h-svh min-h-[580px] min-w-0 flex-col bg-background text-foreground">
      <div className="mx-auto flex min-h-0 w-full max-w-[1440px] flex-1 flex-col gap-3 px-3 py-3 sm:px-6 sm:py-5 lg:px-8">
        <header className="flex shrink-0 items-center justify-between gap-3">
          <a href="#main" className="flex min-w-0 items-center gap-2 rounded-lg focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring" aria-label="Olive and Ember home">
            <span className="flex size-9 shrink-0 items-center justify-center rounded-lg bg-primary text-primary-foreground"><Leaf aria-hidden="true" className="size-5" /></span>
            <span className="min-w-0"><span className="block truncate text-base font-semibold tracking-tight sm:text-lg">Olive &amp; Ember</span><span className="block text-[10px] text-muted-foreground sm:text-xs">A neighborhood table</span></span>
          </a>
          <div className="flex items-center gap-2">
            <Button variant="outline" size="sm" type="button" onClick={toggleTheme} aria-label={darkMode ? 'Switch to light mode' : 'Switch to dark mode'}>
              {darkMode ? <Sun data-icon="inline-start" /> : <Moon data-icon="inline-start" />}
              <span className="hidden sm:inline">{darkMode ? 'Light' : 'Dark'} mode</span>
            </Button>
            <Button variant="outline" size="sm" asChild className="hidden sm:inline-flex">
              <a href="/devui" title="Available in the Development environment">Agent studio <ArrowUpRight data-icon="inline-end" /></a>
            </Button>
          </div>
        </header>

        <section id="main" className="grid min-h-0 min-w-0 flex-1 overflow-hidden rounded-xl border border-border bg-card shadow-sm lg:grid-cols-[minmax(0,1fr)_minmax(270px,310px)]">
          <div className="flex min-h-0 min-w-0 flex-col">
            <div className="flex shrink-0 items-center justify-between gap-3 border-b border-border px-4 py-3 sm:px-6">
              <div className="min-w-0"><h1 className="truncate text-lg font-semibold tracking-tight">Your guest table</h1><p className="text-xs text-muted-foreground">Ready when you are</p></div>
              <Sheet open={historyOpen} onOpenChange={(open) => { setHistoryOpen(open); if (open) refreshHistory(); }}>
                <SheetTrigger asChild><Button variant="outline" size="sm" type="button" aria-label="Open order history"><History data-icon="inline-start" />History</Button></SheetTrigger>
                <SheetContent className="w-full max-w-md sm:max-w-md">
                  <SheetHeader className="border-b border-border">
                    <SheetTitle>Order history</SheetTitle>
                    <SheetDescription>Delivered and cancelled orders, newest first.</SheetDescription>
                  </SheetHeader>
                  <div className="min-h-0 flex-1 overflow-y-auto px-4 pb-4">
                    {historyLoading && <p className="flex items-center gap-2 text-sm text-muted-foreground" role="status"><Spinner />Loading order history…</p>}
                    {!historyLoading && historyError && <ErrorAlert message={historyError} />}
                    {!historyLoading && !historyError && history.length === 0 && <Empty><EmptyHeader><EmptyMedia variant="icon"><History /></EmptyMedia><EmptyTitle>No past orders</EmptyTitle><EmptyDescription>Completed or cancelled orders will appear here after they leave your current order panel.</EmptyDescription></EmptyHeader></Empty>}
                    {!historyLoading && !historyError && history.length > 0 && <div className="flex flex-col gap-3">{history.map((item) => <HistoryCard key={item.id} order={item} />)}</div>}
                  </div>
                </SheetContent>
              </Sheet>
            </div>

            <div className="shrink-0 border-b border-border bg-muted/30 px-4 py-3 sm:px-6">
              <FieldGroup><Field className="gap-1.5">
                <FieldLabel htmlFor="customer-id">Customer ID</FieldLabel>
                <Input id="customer-id" value={customerId} onChange={(event) => changeCustomerId(event.target.value)} autoComplete="off" spellCheck="false" disabled={loading} className="max-w-xs" />
                <FieldDescription>Your preferences and saved orders stay with this ID.</FieldDescription>
              </Field></FieldGroup>
            </div>

            <details className="shrink-0 border-b border-border bg-muted/20 px-4 py-3 lg:hidden">
              <summary className="cursor-pointer list-none rounded-md text-sm font-medium focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring [&::-webkit-details-marker]:hidden">
                <span className="flex items-center justify-between gap-3"><span className="flex shrink-0 items-center gap-1"><ChevronDown className="size-4" aria-hidden="true" />Your order</span><span className="min-w-0 truncate text-xs text-muted-foreground">{order ? `${order.id} · ${order.orderStatus}` : 'No saved order yet'}</span></span>
              </summary>
              <div className="max-h-48 overflow-y-auto pt-3"><OrderPanel order={order} error={orderError} /></div>
            </details>

            <MessageScrollerProvider><MessageScroller className="min-h-0 flex-1">
              <MessageScrollerViewport className="px-4 py-5 sm:px-6">
                <MessageScrollerContent className="mx-auto w-full max-w-3xl gap-4" role="log" aria-label="Conversation messages" aria-live="polite" aria-relevant="additions">
                  {messages.map((message) => <MessageScrollerItem key={message.id}><ChatMessage message={message} /></MessageScrollerItem>)}
                  {loading && <MessageScrollerItem><Message align="start"><MessageAvatar aria-hidden="true"><Leaf className="size-4" /></MessageAvatar><MessageContent><Bubble variant="muted"><BubbleContent><span className="flex items-center gap-2"><Spinner />Putting that together…</span></BubbleContent></Bubble></MessageContent></Message></MessageScrollerItem>}
                  {messages.length === 1 && !loading && <MessageScrollerItem><div className="grid gap-2 sm:grid-cols-2" aria-label="Suggested prompts">{starters.map((item) => <Button key={item.title} type="button" variant="outline" className="h-auto min-h-16 justify-start gap-3 whitespace-normal p-3 text-left" onClick={() => sendMessage(item.prompt)}><item.icon data-icon="inline-start" /><span className="flex min-w-0 flex-col items-start"><span>{item.title}</span><span className="line-clamp-2 text-xs font-normal text-muted-foreground">{item.prompt}</span></span></Button>)}</div></MessageScrollerItem>}
                </MessageScrollerContent>
              </MessageScrollerViewport>
              <MessageScrollerButton />
            </MessageScroller></MessageScrollerProvider>

            <div className="shrink-0 border-t border-border bg-card px-4 py-3 sm:px-6">
              <div className="mx-auto flex max-w-3xl flex-col gap-2">
                {error && <ErrorAlert message={error} />}
                <form onSubmit={(event) => { event.preventDefault(); sendMessage(); }}>
                  <FieldGroup><Field className="gap-1.5">
                    <FieldLabel htmlFor="message">Message Olive &amp; Ember</FieldLabel>
                    <InputGroup className="flex-col items-stretch">
                      <InputGroupTextarea id="message" ref={inputRef} rows={2} value={draft} onChange={(event) => setDraft(event.target.value)} onKeyDown={handleKeyDown} placeholder="Ask about the menu, start an order, or check your status…" disabled={loading} className="max-h-32 min-h-14 resize-y" />
                      <InputGroupAddon align="block-end" className="justify-between"><span className="text-xs text-muted-foreground">Shift + Enter for a new line</span><Button type="submit" size="sm" disabled={loading || !draft.trim()}>{loading ? <Spinner data-icon="inline-start" /> : <Send data-icon="inline-start" />}{loading ? 'Sending' : 'Send'}</Button></InputGroupAddon>
                    </InputGroup>
                  </Field></FieldGroup>
                </form>
                <p className="text-center text-[11px] text-muted-foreground">Local demo only: orders, payment checks, and fulfillment are simulated. Never enter real payment details.</p>
              </div>
            </div>
          </div>

          <aside className="hidden min-h-0 flex-col gap-4 overflow-y-auto border-l border-border bg-muted/20 p-5 lg:flex" aria-label="Current order summary">
            <div><p className="text-xs text-muted-foreground">Order tracker</p><h2 className="text-lg font-semibold tracking-tight">Your order</h2></div>
            <Separator />
            <OrderPanel order={order} error={orderError} />
          </aside>
        </section>
        <footer className="flex shrink-0 items-center justify-between gap-2 text-xs text-muted-foreground"><span>Olive &amp; Ember · Something good is cooking</span><a href="/devui" className="underline underline-offset-4 sm:hidden">Agent studio</a></footer>
      </div>
    </main>
  );
}

function ErrorAlert({ message }) {
  return <Alert variant="destructive" role="alert"><AlertDescription className="break-words">{message}</AlertDescription></Alert>;
}

function ChatMessage({ message }) {
  const isUser = message.role === 'user';
  return <Message align={isUser ? 'end' : 'start'}>
    <MessageAvatar aria-label={isUser ? 'You' : 'Olive and Ember'}>{isUser ? <span className="text-xs font-medium">You</span> : <Leaf className="size-4" aria-hidden="true" />}</MessageAvatar>
    <MessageContent><Bubble variant={isUser ? 'default' : 'muted'} align={isUser ? 'end' : 'start'}><BubbleContent className="whitespace-pre-wrap">{message.text}</BubbleContent></Bubble></MessageContent>
  </Message>;
}

function HistoryCard({ order }) {
  return <Card size="sm">
    <CardHeader><CardTitle className="break-all">Order {order.id}</CardTitle><CardDescription>{new Date(order.createdAtUtc).toLocaleString()}</CardDescription><Badge variant="secondary" className="mt-2 w-fit capitalize">{order.orderStatus}</Badge></CardHeader>
    <CardContent className="flex flex-col gap-3"><p className="whitespace-pre-wrap break-words text-sm">{order.summary || 'No item details were saved.'}</p><Separator /><div className="grid grid-cols-2 gap-3 text-xs"><div><p className="font-medium">Payment</p><p className="capitalize">{order.paymentMethod}</p><p className="text-muted-foreground">{order.paymentStatus}</p></div><div><p className="font-medium">Delivery</p><p className="capitalize text-muted-foreground">{order.deliveryStatus}</p></div></div></CardContent>
  </Card>;
}

function OrderPanel({ order, error }) {
  if (!order) return <div className="flex flex-col gap-3">{error && <ErrorAlert message={error} />}<Empty><EmptyHeader><EmptyMedia variant="icon"><Clock3 /></EmptyMedia><EmptyTitle>No saved order yet</EmptyTitle><EmptyDescription>Your saved order and agreed choices will appear here when checkout creates a draft. Only details saved with this customer ID are shown.</EmptyDescription></EmptyHeader></Empty></div>;

  return <div className="flex flex-col gap-4">
    {error && <ErrorAlert message={`Could not refresh: ${error}`} />}
    <Card size="sm"><CardHeader><CardTitle className="break-all">Order {order.id}</CardTitle><CardDescription>Saved {new Date(order.createdAtUtc).toLocaleString()}</CardDescription></CardHeader><CardContent><Badge variant="secondary" className="capitalize">{order.orderStatus}</Badge></CardContent></Card>
    <section className="flex flex-col gap-2"><h3 className="text-sm font-medium">Agreed order</h3><p className="whitespace-pre-wrap break-words text-sm text-muted-foreground">{order.summary || 'No item details were saved.'}</p></section>
    <Separator />
    <section className="flex flex-col gap-1"><h3 className="text-sm font-medium">Payment choice</h3><p className="text-sm capitalize">{order.paymentMethod}</p><p className="text-sm text-muted-foreground">{order.paymentStatus}</p><p className="text-xs text-muted-foreground">Demo only. No payment was collected.</p></section>
    <Separator />
    <section className="flex flex-col gap-1"><h3 className="text-sm font-medium">Simulated delivery</h3><p className="text-sm capitalize">{order.deliveryStatus}</p><p className="text-xs text-muted-foreground">Status refreshes while this chat is open.</p></section>
  </div>;
}
