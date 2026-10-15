import DestinationPage from './modules/destinations/DestinationPage.jsx';

function BrandIcon() {
  return (
    <svg width="25" height="25" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.7" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
      <circle cx="12" cy="12" r="9" />
      <path d="m15.5 8.5-2.3 4.7-4.7 2.3 2.3-4.7 4.7-2.3Z" />
    </svg>
  );
}

export default function App() {
  return (
    <div className="app-shell">
      <header className="site-header">
        <a href="#main" className="brand" aria-label="Wayfinder, skip to destination search">
          <span className="brand-mark"><BrandIcon /></span>
          <span>wayfinder<span className="brand-period">.</span></span>
        </a>
        <span className="header-note">A starting point for everywhere</span>
      </header>
      <DestinationPage />
    </div>
  );
}
